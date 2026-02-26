using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.Model.Room;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Infrastructure.Redis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Splendor_Game_Server.Hubs
{
    [Authorize]
    public class GameHub : Hub
    {
        private readonly ISplendorService _gameService;
        private readonly IRedisMapper _redisMapper;
        private readonly IUserConnectionService _userConnectionService;
        private readonly ILogger<GameHub> _logger;

        public GameHub(
            ISplendorService gameService,
            IRedisMapper redisMapper,
            IUserConnectionService userConnectionService,
            ILogger<GameHub> logger)
        {
            _gameService = gameService;
            _redisMapper = redisMapper;
            _userConnectionService = userConnectionService;
            _logger = logger;
        }

        public async Task<object> JoinGame(string gameId, string playerId)
        {
            try
            {
                _logger.LogInformation("🎮 Player {PlayerId} joining game {GameId}", playerId, gameId);

                var players = await _redisMapper.GetPlayers(gameId);
                if (!players.ContainsKey(playerId))
                {
                    throw new HubException("You are not a participant of this game");
                }

                await Groups.AddToGroupAsync(Context.ConnectionId, $"game:{gameId}");
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, gameId);

                var info = await _redisMapper.GetGameInfo(gameId);
                var board = await _redisMapper.GetBoard(gameId);
                var turn = await _redisMapper.GetTurn(gameId);
                var cardDecks = await _redisMapper.GetCardDecks(gameId);

                var response = new
                {
                    info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                    players = players.ToDictionary(
                        kv => kv.Key,
                        kv => JsonSerializer.Deserialize<object>(kv.Value)
                    ),
                    board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                    turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null,
                    cardDecks = cardDecks != null ? JsonSerializer.Deserialize<object>(cardDecks) : null,
                };

                await Clients.Caller.SendAsync("GameStateLoaded", response);

                // Notify others
                //await Clients.OthersInGroup($"game:{gameId}").SendAsync("PlayerJoined", new
                //{
                //    playerId = playerId,
                //    timestamp = DateTime.UtcNow
                //});

                _logger.LogInformation("✅ Player {PlayerId} joined game {GameId}", playerId, gameId);
                return new { room = response, success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error joining game {GameId} for player {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "JOIN_GAME_ERROR" });
                return  new { message = ex.Message, success = false};
            }
        }

       

        public async Task LeaveGame(string roomCode, string playerId)
        {
            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"game:{roomCode}");
                await _userConnectionService.RemoveConnection(Context.ConnectionId);

                await Clients.Group($"game:{roomCode}").SendAsync("PlayerLeft", new
                {
                    playerId = playerId,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("👋 Player {PlayerId} left game {RoomCode}", playerId, roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error leaving game {RoomCode}", roomCode);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection != null && !string.IsNullOrEmpty(userConnection.RoomId))
                {
                    _logger.LogWarning("🔌 Player {PlayerId} disconnected from game {GameId}",
                        userConnection.PlayerId, userConnection.RoomId);

                    await Clients.Group($"game:{userConnection.RoomId}").SendAsync("PlayerDisconnected", new
                    {
                        playerId = userConnection.PlayerId,
                        timestamp = DateTime.UtcNow
                    });

                    await _userConnectionService.RemoveConnection(Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling disconnect");
            }

            await base.OnDisconnectedAsync(exception);
        }
        public async Task<object> CollectGem(string gameId, string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                var result = await _gameService.CollectGemsAsync(gameId, playerId, gems);
                var newGameState = BroadcastGameState(gameId);
                await Clients.Group($"Game_{gameId}")
                   .SendAsync("TurnChange", new { message = result });
                return new { result, success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting gem {GameId} {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "COLLECT_GEM_ERROR" });
                return new { message = ex.Message, success = false };
            }
        }

        private async Task BroadcastGameState(string roomCode)
        {
            try
            {
                var info = await _redisMapper.GetGameInfo(roomCode);
                var players = await _redisMapper.GetPlayers(roomCode);
                var board = await _redisMapper.GetBoard(roomCode);
                var turn = await _redisMapper.GetTurn(roomCode);
                var cardDecks = await _redisMapper.GetCardDecks(roomCode);

                var response = new
                {
                    info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                    players = players.ToDictionary(
                        kv => kv.Key,
                        kv => JsonSerializer.Deserialize<object>(kv.Value)
                    ),
                    board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                    turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null,
                    cardDecks = cardDecks != null ? JsonSerializer.Deserialize<object>(cardDecks) : null,
                };

                await Clients.Group($"game:{roomCode}").SendAsync("GameStateUpdated", response);

                _logger.LogDebug("📡 Game state broadcasted for {RoomCode}", roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error broadcasting game state for {RoomCode}", roomCode);
                throw;
            }
        }
    }
}
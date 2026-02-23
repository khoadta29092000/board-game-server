using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Infrastructure.Redis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using CleanArchitecture.Application.IRepository;

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

        #region Connection Management

        public async Task JoinGame(string gameId, string playerId)
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

                var response = new
                {
                    info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                    players = players.ToDictionary(
                        kv => kv.Key,
                        kv => JsonSerializer.Deserialize<object>(kv.Value)
                    ),
                    board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                    turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null
                };

                await Clients.Caller.SendAsync("GameStateLoaded", response);

                // Notify others
                //await Clients.OthersInGroup($"game:{gameId}").SendAsync("PlayerJoined", new
                //{
                //    playerId = playerId,
                //    timestamp = DateTime.UtcNow
                //});

                _logger.LogInformation("✅ Player {PlayerId} joined game {GameId}", playerId, gameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error joining game {GameId} for player {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "JOIN_GAME_ERROR" });
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

        #endregion

        #region Game Actions

        public async Task CollectGems(string roomCode, string playerId, Dictionary<string, int> gems)
        {
            try
            {
                _logger.LogInformation("💎 Player {PlayerId} collecting gems in {RoomCode}: {@Gems}",
                    playerId, roomCode, gems);

                // Convert string keys to GemColor enum
                var gemDict = gems.ToDictionary(
                    kv => Enum.Parse<GemColor>(kv.Key, ignoreCase: true),
                    kv => kv.Value
                );

                var result = await _gameService.CollectGemsAsync(roomCode, playerId, gemDict);

                // Broadcast success to all players in game
                await Clients.Group($"game:{roomCode}").SendAsync("GemsCollected", new
                {
                    success = true,
                    playerId = playerId,
                    gems = gems,
                    result = result,
                    timestamp = DateTime.UtcNow
                });

                await BroadcastGameState(roomCode);

                _logger.LogInformation("✅ Gems collected successfully by {PlayerId}", playerId);
            }
            catch (GameNotFoundException ex)
            {
                _logger.LogWarning("⚠️ Game not found: {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "GAME_NOT_FOUND" });
            }
            catch (NotYourTurnException ex)
            {
                _logger.LogWarning("⚠️ Not player {PlayerId}'s turn", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "NOT_YOUR_TURN" });
            }
            catch (InvalidGemCollectionException ex)
            {
                _logger.LogWarning("⚠️ Invalid gem collection by {PlayerId}: {Message}", playerId, ex.Message);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "INVALID_GEM_COLLECTION" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting gems for {PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        public async Task DiscardGems(string roomCode, string playerId, Dictionary<string, int> gems)
        {
            try
            {
                _logger.LogInformation("🗑️ Player {PlayerId} discarding gems in {RoomCode}: {@Gems}",
                    playerId, roomCode, gems);

                var gemDict = gems.ToDictionary(
                    kv => Enum.Parse<GemColor>(kv.Key, ignoreCase: true),
                    kv => kv.Value
                );

                var success = await _gameService.DiscardGemsAsync(roomCode, playerId, gemDict);

                await Clients.Group($"game:{roomCode}").SendAsync("GemsDiscarded", new
                {
                    success = success,
                    playerId = playerId,
                    discardedGems = gems,
                    timestamp = DateTime.UtcNow
                });

                await BroadcastGameState(roomCode);

                _logger.LogInformation("✅ Gems discarded successfully by {PlayerId}", playerId);
            }
            catch (GameNotFoundException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "GAME_NOT_FOUND" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error discarding gems for {PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        public async Task PurchaseCard(string roomCode, string playerId, string cardId)
        {
            try
            {
                _logger.LogInformation("🃏 Player {PlayerId} purchasing card {CardId} in {RoomCode}",
                    playerId, cardId, roomCode);

                var cardGuid = Guid.Parse(cardId);
                var success = await _gameService.PurchaseCardAsync(roomCode, playerId, cardGuid);

                await Clients.Group($"game:{roomCode}").SendAsync("CardPurchased", new
                {
                    success = success,
                    playerId = playerId,
                    cardId = cardId,
                    timestamp = DateTime.UtcNow
                });

                await BroadcastGameState(roomCode);

                _logger.LogInformation("✅ Card purchased successfully by {PlayerId}", playerId);
            }
            catch (GameNotFoundException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "GAME_NOT_FOUND" });
            }
            catch (NotYourTurnException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "NOT_YOUR_TURN" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error purchasing card for {PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        public async Task ReserveCard(string roomCode, string playerId, string? cardId = null, int? level = null)
        {
            try
            {
                _logger.LogInformation("📌 Player {PlayerId} reserving card in {RoomCode}. CardId: {CardId}, Level: {Level}",
                    playerId, roomCode, cardId, level);

                Guid cardGuid = cardId != null ? Guid.Parse(cardId) : Guid.Empty;
                var success = await _gameService.ReserveCardAsync(roomCode, playerId, cardGuid, level);

                await Clients.Group($"game:{roomCode}").SendAsync("CardReserved", new
                {
                    success = success,
                    playerId = playerId,
                    cardId = cardId,
                    level = level,
                    timestamp = DateTime.UtcNow
                });

                await BroadcastGameState(roomCode);

                _logger.LogInformation("✅ Card reserved successfully by {PlayerId}", playerId);
            }
            catch (GameNotFoundException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "GAME_NOT_FOUND" });
            }
            catch (NotYourTurnException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "NOT_YOUR_TURN" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error reserving card for {PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        public async Task SelectNoble(string roomCode, string playerId, string nobleId)
        {
            try
            {
                _logger.LogInformation("👑 Player {PlayerId} selecting noble {NobleId} in {RoomCode}",
                    playerId, nobleId, roomCode);

                var nobleGuid = Guid.Parse(nobleId);
                var success = await _gameService.SelectNobleAsync(roomCode, playerId, nobleGuid);

                await Clients.Group($"game:{roomCode}").SendAsync("NobleSelected", new
                {
                    success = success,
                    playerId = playerId,
                    nobleId = nobleId,
                    timestamp = DateTime.UtcNow
                });

                await BroadcastGameState(roomCode);

                _logger.LogInformation("✅ Noble selected successfully by {PlayerId}", playerId);
            }
            catch (GameNotFoundException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "GAME_NOT_FOUND" });
            }
            catch (InvalidTurnPhaseException ex)
            {
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "INVALID_PHASE" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error selecting noble for {PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        #endregion

        #region Query Methods

        public async Task GetGameState(string roomCode)
        {
            try
            {
                _logger.LogInformation("📊 Fetching game state for {RoomCode}", roomCode);
                await BroadcastGameState(roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching game state for {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        //public async Task GetEligibleNobles(string roomCode, string playerId)
        //{
        //    try
        //    {
        //        _logger.LogInformation("👑 Fetching eligible nobles for {PlayerId} in {RoomCode}", playerId, roomCode);

        //        var eligibleNobles = await _gameService.GetEligibleNoblesAsync(roomCode, playerId);

        //        await Clients.Caller.SendAsync("EligibleNobles", new
        //        {
        //            playerId = playerId,
        //            nobles = eligibleNobles,
        //            timestamp = DateTime.UtcNow
        //        });

        //        _logger.LogInformation("✅ Found {Count} eligible nobles for {PlayerId}", eligibleNobles.Count, playerId);
        //    }
        //    catch (GameNotFoundException ex)
        //    {
        //        await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "GAME_NOT_FOUND" });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "❌ Error fetching eligible nobles for {PlayerId}", playerId);
        //        await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
        //    }
        //}

        public async Task GetPlayerHand(string roomCode, string playerId)
        {
            try
            {
                _logger.LogInformation("🃏 Fetching player hand for {PlayerId} in {RoomCode}", playerId, roomCode);

                var playerData = await _redisMapper.GetPlayers(roomCode);

                await Clients.Caller.SendAsync("PlayerHand", new
                {
                    playerId = playerId,
                    data = playerData,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching player hand for {PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "UNKNOWN_ERROR" });
            }
        }

        #endregion

        #region Helper Methods

        private async Task BroadcastGameState(string roomCode)
        {
            try
            {
                var info = await _redisMapper.GetGameInfo(roomCode);
                var players = await _redisMapper.GetPlayers(roomCode);
                var board = await _redisMapper.GetBoard(roomCode);
                var turn = await _redisMapper.GetTurn(roomCode);

                var response = new
                {
                    info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                    players = players.ToDictionary(
                        kv => kv.Key,
                        kv => JsonSerializer.Deserialize<object>(kv.Value)
                    ),
                    board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                    turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null,
                    timestamp = DateTime.UtcNow
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

        #endregion
    }
}
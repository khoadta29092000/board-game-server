using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.DTO.Splendor;
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
        private readonly IGameHistoryService _historyService;
        private readonly ILogger<GameHub> _logger;

        public GameHub(
            ISplendorService gameService,
            IRedisMapper redisMapper,
            IUserConnectionService userConnectionService,
            IGameHistoryService historyService,
            ILogger<GameHub> logger)
        {
            _gameService = gameService;
            _redisMapper = redisMapper;
            _userConnectionService = userConnectionService;
            _historyService = historyService;
            _logger = logger;
        }

        // =====================================================================
        // JOIN GAME
        // Thứ tự: Redis trước → nếu không có thì check MongoDB history
        // =====================================================================
        public async Task<object> JoinGame(string gameId, string playerId)
        {
            try
            {
                _logger.LogInformation("🎮 Player {PlayerId} joining game {GameId}", playerId, gameId);

                await Groups.AddToGroupAsync(Context.ConnectionId, $"game:{gameId}");
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, gameId);

                // 1. Thử load từ Redis (game đang chạy)
                var players = await _redisMapper.GetPlayers(gameId);
                if (players != null && players.ContainsKey(playerId))
                {
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
                    _logger.LogInformation("✅ Player {PlayerId} joined game {GameId} from Redis", playerId, gameId);
                    return new { success = true };
                }

                // 2. Redis không có → check MongoDB history (game đã kết thúc)
                var history = await _historyService.GetByGameIdAsync(gameId);
                if (history != null && history.State == "Completed")
                {
                    // Trả về minimal state để FE hiện GameOverOverlay
                    var completedResponse = new
                    {
                        info = new
                        {
                            gameId = history.GameId,
                            state = history.State,
                            winnerId = history.WinnerId,
                            players = history.PlayerOrder,
                            completedAt = history.CompletedAt
                        },
                        players = history.Players.ToDictionary(
                            kv => kv.Key,
                            kv => (object)new
                            {
                                playerId = kv.Value.PlayerId,
                                name = kv.Value.Name,
                                points = kv.Value.Score,
                                rank = kv.Value.Rank,
                                isWinner = kv.Value.IsWinner,
                                totalOwnedCards = kv.Value.Stats.Contains("purchasedCards")
                                ? kv.Value.Stats["purchasedCards"].AsInt32
                                : 0,
                                // Đọc gems dict từ Stats
                                gems = kv.Value.Stats.Contains("gems") && kv.Value.Stats["gems"].IsBsonDocument
                                ? kv.Value.Stats["gems"].AsBsonDocument
                                    .ToDictionary(e => e.Name, e => e.Value.ToInt32())
                                : new Dictionary<string, int>(),
                                // Đọc bonuses dict từ Stats
                                bonuses = kv.Value.Stats.Contains("bonuses") && kv.Value.Stats["bonuses"].IsBsonDocument
                                ? kv.Value.Stats["bonuses"].AsBsonDocument
                                    .ToDictionary(e => e.Name, e => e.Value.ToInt32())
                                : new Dictionary<string, int>(),
                                reservedCards = new List<object>(),
                                purchasedCards = new List<object>(),
                            }
                        ),
                        board = (object?)null,
                        turn = (object?)null,
                        cardDecks = (object?)null,
                    };


                    await Clients.Caller.SendAsync("GameStateLoaded", completedResponse);
                    _logger.LogInformation("✅ Player {PlayerId} rejoined completed game {GameId} from MongoDB", playerId, gameId);
                    return new { success = true };
                }

                // 3. Không tìm thấy
                throw new HubException("Game not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error joining game {GameId} for player {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "JOIN_GAME_ERROR" });
                return new { message = ex.Message, success = false };
            }
        }

        // =====================================================================
        // LEAVE GAME
        // =====================================================================
        public async Task LeaveGame(string roomCode, string playerId)
        {
            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"game:{roomCode}");
                await _userConnectionService.RemoveConnection(Context.ConnectionId);

                await Clients.Group($"game:{roomCode}").SendAsync("PlayerLeft", new
                {
                    playerId,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("👋 Player {PlayerId} left game {RoomCode}", playerId, roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error leaving game {RoomCode}", roomCode);
            }
        }

        // =====================================================================
        // DISCONNECT
        // =====================================================================
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

        // =====================================================================
        // COLLECT GEM
        // =====================================================================
        public async Task<object> CollectGem(string gameId, string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                CollectGemResult result = await _gameService.CollectGemsAsync(gameId, playerId, gems);

                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Cannot collect gems.", code = "COLLECT_GEM_ERROR" });
                    return new { success = false };
                }

                await BroadcastGameState(gameId);

                if (result.NeedsDiscard)
                {
                    await Clients.Caller.SendAsync("NeedDiscard", new
                    {
                        currentGems = result.CurrentGems,
                        excessCount = result.TotalGems - 10
                    });
                }

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting gem {GameId} {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "COLLECT_GEM_ERROR" });
                return new { success = false, message = ex.Message };
            }
        }

        // =====================================================================
        // DISCARD GEM
        // =====================================================================
        public async Task<object> DiscardGem(string gameId, string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                bool success = await _gameService.DiscardGemsAsync(gameId, playerId, gems);

                if (!success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Invalid discard action.", code = "DISCARD_GEM_ERROR" });
                    return new { success = false };
                }

                await BroadcastGameState(gameId);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error discarding gem {GameId} {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "DISCARD_GEM_ERROR" });
                return new { success = false, message = ex.Message };
            }
        }

        // =====================================================================
        // PURCHASE CARD
        // =====================================================================
        public async Task<object> PurchaseCard(string gameId, string playerId, Guid cardId)
        {
            try
            {
                PurchaseCardResult result = await _gameService.PurchaseCardAsync(gameId, playerId, cardId);

                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Cannot purchase card.", code = "PURCHASE_CARD_ERROR" });
                    return new { success = false };
                }

                if (result.IsGameOver)
                {
                    // Redis đã xóa → broadcast từ MongoDB
                    await BroadcastGameOverState(gameId, result.Winner);
                }
                else
                {
                    await BroadcastGameState(gameId);

                    if (result.JustTriggeredLastRound)
                    {
                        await Clients.Group($"game:{gameId}").SendAsync("LastRound", new
                        {
                            triggeredBy = playerId
                        });
                    }

                    if (result.NeedsSelectNoble)
                    {
                        await Clients.Caller.SendAsync("NeedSelectNoble", new
                        {
                            eligibleNobles = result.EligibleNobles
                        });
                    }
                }

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error purchasing card {GameId} {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "PURCHASE_CARD_ERROR" });
                return new { success = false, message = ex.Message };
            }
        }

        // =====================================================================
        // SELECT NOBLE
        // =====================================================================
        public async Task<object> SelectNoble(string gameId, string playerId, Guid nobleId)
        {
            try
            {
                SelectNobleResult result = await _gameService.SelectNobleAsync(gameId, playerId, nobleId);

                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Cannot select noble.", code = "SELECT_NOBLE_ERROR" });
                    return new { success = false };
                }

                if (result.IsGameOver)
                {
                    // Redis đã xóa → broadcast từ MongoDB
                    await BroadcastGameOverState(gameId, result.Winner);
                }
                else
                {
                    await BroadcastGameState(gameId);

                    if (result.JustTriggeredLastRound)
                    {
                        await Clients.Group($"game:{gameId}").SendAsync("LastRound", new
                        {
                            triggeredBy = playerId
                        });
                    }
                }

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error selecting noble {GameId} {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "SELECT_NOBLE_ERROR" });
                return new { success = false, message = ex.Message };
            }
        }

        // =====================================================================
        // RESERVE CARD
        // =====================================================================
        public async Task<object> ReserveCard(string gameId, string playerId, Guid? cardId, int? level = null)
        {
            try
            {
                bool success = await _gameService.ReserveCardAsync(gameId, playerId, cardId, level);

                if (!success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Cannot reserve card.", code = "RESERVE_CARD_ERROR" });
                    return new { success = false };
                }

                await BroadcastGameState(gameId);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error reserving card {GameId} {PlayerId}", gameId, playerId);
                await Clients.Caller.SendAsync("Error", new { message = ex.Message, code = "RESERVE_CARD_ERROR" });
                return new { success = false, message = ex.Message };
            }
        }

        // =====================================================================
        // BROADCAST GAME STATE — từ Redis (game đang chạy)
        // =====================================================================
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

        // =====================================================================
        // BROADCAST GAME OVER STATE — từ MongoDB (Redis đã xóa)
        // Gửi GameStateUpdated (state=Completed) + GameOver event
        // =====================================================================
        private async Task BroadcastGameOverState(string roomCode, string? winnerId)
        {
            try
            {
                var history = await _historyService.GetByGameIdAsync(roomCode);
                if (history == null)
                {
                    _logger.LogWarning("⚠️ Game history not found for {RoomCode} after game over", roomCode);
                    return;
                }

                var completedResponse = new
                {
                    info = new
                    {
                        gameId = history.GameId,
                        state = history.State,
                        winnerId = history.WinnerId,
                        players = history.PlayerOrder,
                        completedAt = history.CompletedAt
                    },
                    players = history.Players.ToDictionary(
                        kv => kv.Key,
                        kv => (object)new
                        {
                            playerId = kv.Value.PlayerId,
                            name = kv.Value.Name,
                            points = kv.Value.Score,
                            rank = kv.Value.Rank,
                            isWinner = kv.Value.IsWinner,
                            totalOwnedCards = kv.Value.Stats.Contains("purchasedCards")
                                ? kv.Value.Stats["purchasedCards"].AsInt32
                                : 0,
                            // Đọc gems dict từ Stats
                            gems = kv.Value.Stats.Contains("gems") && kv.Value.Stats["gems"].IsBsonDocument
                                ? kv.Value.Stats["gems"].AsBsonDocument
                                    .ToDictionary(e => e.Name, e => e.Value.ToInt32())
                                : new Dictionary<string, int>(),
                            // Đọc bonuses dict từ Stats
                            bonuses = kv.Value.Stats.Contains("bonuses") && kv.Value.Stats["bonuses"].IsBsonDocument
                                ? kv.Value.Stats["bonuses"].AsBsonDocument
                                    .ToDictionary(e => e.Name, e => e.Value.ToInt32())
                                : new Dictionary<string, int>(),
                            reservedCards = new List<object>(),
                            purchasedCards = new List<object>(),
                        }
                    ),
                    board = (object?)null,
                    turn = (object?)null,
                    cardDecks = (object?)null,
                };

                await Clients.Group($"game:{roomCode}").SendAsync("GameStateUpdated", completedResponse);
                await Clients.Group($"game:{roomCode}").SendAsync("GameOver", new { winner = winnerId });

                _logger.LogInformation("🏆 Game over broadcasted for {RoomCode}, winner: {WinnerId}", roomCode, winnerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error broadcasting game over state for {RoomCode}", roomCode);
                throw;
            }
        }
    }
}
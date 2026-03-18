using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.System;
using StackExchange.Redis;

using System.Text.Json;

namespace CleanArchitecture.Infrastructure.Redis
{
    public class GameRedisMapper : IRedisMapper
    {
        private readonly IDatabase _redis;
        private readonly JsonSerializerOptions _jsonOptions;

        public GameRedisMapper(IConnectionMultiplexer redis)
        {
            _redis = redis.GetDatabase();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Sync game state sang Redis với format client-friendly
        /// </summary>
        public async Task SyncGameStateToRedis(GameContext context, string gameId)
        {
            await Task.WhenAll(
                SaveGameInfo(context, gameId),
                SavePlayers(context, gameId),
                SaveBoard(context, gameId),
                SaveTurn(context, gameId)
            );
        }

        // ============== game:{gameId}:info ==============
        private async Task SaveGameInfo(GameContext context, string gameId)
        {
            var session = context.GameSession;
            var boardEntity = context.GetEntity<BoardEntity>(session.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            var playerIds = session.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id)?.GetComponent<PlayerComponent>()?.PlayerId)
                .Where(p => p != null)
                .ToList();

            var info = new
            {
                gameId,
                state = session.Status.ToString(),
                currentTurn = turnComp?.CurrentPlayerIndex ?? 0,
                players = playerIds,
                createdAt = session.CreatedAt,
                startedAt = session.StartedAt,
                completedAt = session.CompletedAt,
                winnerId = session.WinnerId
            };

            await _redis.StringSetAsync($"game:{gameId}:info",JsonSerializer.Serialize(info, _jsonOptions),expiry: TimeSpan.FromHours(1));

        }

        // ============== game:{gameId}:players (Hash) ==============
        private async Task SavePlayers(GameContext context, string gameId)
        {
            var entries = new List<HashEntry>();

            foreach (var playerEntityId in context.GameSession.PlayerEntityIds)
            {
                var playerEntity = context.GetEntity<PlayerEntity>(playerEntityId);
                var playerComp = playerEntity?.GetComponent<PlayerComponent>();
                if (playerComp == null) continue;

                // Map reserved cards với full info
                var reservedCardsInfo = playerComp.ReservedCards
                    .Select(cardId => MapCardForClient(context, cardId))
                    .Where(c => c != null)
                    .ToList();

                var purcharseCardsInfo = playerComp.PurchaseCards
                    .Select(cardId => MapCardForClient(context, cardId))
                    .Where(c => c != null)
                    .ToList();

                var playerData = new
                {
                    playerId = playerComp.PlayerId,
                    name = playerComp.Name,
                    points = playerComp.PrestigePoints,
                    gems = playerComp.Gems,
                    bonuses = playerComp.Bonuses,
                    reservedCards = reservedCardsInfo,
                    purchasedCards = purcharseCardsInfo,
                    // Note: ownedCards có thể tính từ bonuses nếu không track riêng
                    totalOwnedCards = playerComp.Bonuses.Values.Sum()
                };

                entries.Add(new HashEntry(
                    playerComp.PlayerId,
                    JsonSerializer.Serialize(playerData, _jsonOptions)
                ));
            }

            if (entries.Any())
            {
                await _redis.HashSetAsync($"game:{gameId}:players", entries.ToArray());
                await _redis.KeyExpireAsync(                        
                    $"game:{gameId}:players",
                    TimeSpan.FromHours(1)
                );
            }
        }

        // ============== game:{gameId}:board ==============
        private async Task SaveBoard(GameContext context, string gameId)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var boardComp = boardEntity?.GetComponent<BoardComponent>();
            if (boardComp == null) return;

            var boardData = new
            {
                gemBank = boardComp.AvailableGems,

                // Visible cards: Gửi đầy đủ info
                visibleCards = new
                {
                    level1 = boardComp.VisibleCards.GetValueOrDefault(1, new List<Guid>())
                        .Select(id => MapCardForClient(context, id))
                        .Where(c => c != null)
                        .ToList(),
                    level2 = boardComp.VisibleCards.GetValueOrDefault(2, new List<Guid>())
                        .Select(id => MapCardForClient(context, id))
                        .Where(c => c != null)
                        .ToList(),
                    level3 = boardComp.VisibleCards.GetValueOrDefault(3, new List<Guid>())
                        .Select(id => MapCardForClient(context, id))
                        .Where(c => c != null)
                        .ToList()
                },

                // Card decks: CHỈ gửi số lượng (không leak thứ tự)
                cardDecks = new
                {
                    level1 = boardComp.CardDecks.GetValueOrDefault(1, new Queue<Guid>()).Count,
                    level2 = boardComp.CardDecks.GetValueOrDefault(2, new Queue<Guid>()).Count,
                    level3 = boardComp.CardDecks.GetValueOrDefault(3, new Queue<Guid>()).Count
                },

                // Nobles: Gửi đầy đủ info
                nobles = boardComp.VisibleNobles
                    .Select(id => MapNobleForClient(context, id))
                    .Where(n => n != null)
                    .ToList()
            };

            await _redis.StringSetAsync(
                $"game:{gameId}:board",
                JsonSerializer.Serialize(boardData, _jsonOptions),
                expiry: TimeSpan.FromHours(1)
            );
        }

        // ============== game:{gameId}:turn ==============
        private async Task SaveTurn(GameContext context, string gameId)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            if (turnComp == null) return;

            var currentPlayerEntityId = context.GameSession.PlayerEntityIds[turnComp.CurrentPlayerIndex];
            var currentPlayerEntity = context.GetEntity<PlayerEntity>(currentPlayerEntityId);
            var currentPlayerComp = currentPlayerEntity?.GetComponent<PlayerComponent>();

            var turnData = new
            {
                currentPlayer = currentPlayerComp?.PlayerId,
                currentPlayerIndex = turnComp.CurrentPlayerIndex,
                phase = turnComp.Phase.ToString(),
                turnNumber = turnComp.TurnNumber, // Simplified
                isLastRound = turnComp.IsLastRound,
                lastActionTime = turnComp.LastActionTime
            };

            await _redis.StringSetAsync(
                $"game:{gameId}:turn",
                JsonSerializer.Serialize(turnData, _jsonOptions),
                expiry: TimeSpan.FromHours(1)
            );
        }

        // ============== HELPER METHODS ==============

        private object? MapCardForClient(GameContext context, Guid cardId)
        {
            var cardEntity = context.GetEntity<CardEntity>(cardId);
            var cardComp = cardEntity?.GetComponent<CardComponent>();
            if (cardComp == null) return null;

            return new
            {
                cardId = cardId.ToString(),
                level = cardComp.Level,
                points = cardComp.PrestigePoints,
                bonusColor = cardComp.BonusColor.ToString(),
                cost = cardComp.Cost
            };
        }

        private object? MapNobleForClient(GameContext context, Guid nobleId)
        {
            var nobleEntity = context.GetEntity<NobleEntity>(nobleId);
            var nobleComp = nobleEntity?.GetComponent<NobleComponent>();
            if (nobleComp == null) return null;

            return new
            {
                nobleId = nobleId.ToString(),
                points = nobleComp.PrestigePoints,
                requirements = nobleComp.Requirements
            };
        }

        // ============== GET METHODS ==============

        public async Task<string?> GetGameInfo(string gameId)
        {
            return await _redis.StringGetAsync($"game:{gameId}:info");
        }

        public async Task<Dictionary<string, string>> GetPlayers(string gameId)
        {
            var hash = await _redis.HashGetAllAsync($"game:{gameId}:players");
            return hash.ToDictionary(
                h => h.Name.ToString(),
                h => h.Value.ToString()
            );
        }

        public async Task<string?> GetBoard(string gameId)
        {
            return await _redis.StringGetAsync($"game:{gameId}:board");
        }

        public async Task<string?> GetCardDecks(string gameId)
        {
            var boardJson = await GetBoard(gameId);
            if (string.IsNullOrEmpty(boardJson))
                return null;
            try
            {
                using var doc = JsonDocument.Parse(boardJson);
                var root = doc.RootElement;

                // Lấy từ board.visibleCards
                var board = root.GetProperty("board");
                var visibleCards = board.GetProperty("visibleCards");
                int level1Visible = visibleCards.GetProperty("level1").GetArrayLength();
                int level2Visible = visibleCards.GetProperty("level2").GetArrayLength();
                int level3Visible = visibleCards.GetProperty("level3").GetArrayLength();

                // Đếm reservedCards từ players (object, không phải array)
                int level1Used = 0, level2Used = 0, level3Used = 0;

                if (root.TryGetProperty("players", out var players))
                {
                    // players là Dictionary → dùng EnumerateObject()
                    foreach (var playerProp in players.EnumerateObject())
                    {
                        var player = playerProp.Value;

                        // reservedCards: array of card objects có field "level"
                        if (player.TryGetProperty("reservedCards", out var reservedCards))
                        {
                            foreach (var card in reservedCards.EnumerateArray())
                            {
                                if (card.TryGetProperty("level", out var levelProp))
                                {
                                    var level = levelProp.GetInt32();
                                    if (level == 1) level1Used++;
                                    else if (level == 2) level2Used++;
                                    else if (level == 3) level3Used++;
                                }
                            }
                        }

                        // totalOwnedCards không đủ chi tiết theo level
                        // Nếu backend trả về purchasedCards array thì xử lý tương tự reservedCards
                        if (player.TryGetProperty("purchasedCards", out var purchasedCards))
                        {
                            foreach (var card in purchasedCards.EnumerateArray())
                            {
                                if (card.TryGetProperty("level", out var levelProp))
                                {
                                    var level = levelProp.GetInt32();
                                    if (level == 1) level1Used++;
                                    else if (level == 2) level2Used++;
                                    else if (level == 3) level3Used++;
                                }
                            }
                        }
                    }
                }

                const int TOTAL_LEVEL1 = 40;
                const int TOTAL_LEVEL2 = 30;
                const int TOTAL_LEVEL3 = 20;

                var result = new
                {
                    level1 = TOTAL_LEVEL1 - level1Visible - level1Used,
                    level2 = TOTAL_LEVEL2 - level2Visible - level2Used,
                    level3 = TOTAL_LEVEL3 - level3Visible - level3Used
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GetCardDecks error: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetTurn(string gameId)
        {
            return await _redis.StringGetAsync($"game:{gameId}:turn");
        }

        public async Task DeleteGame(string gameId)
        {
            var keys = new[]
            {
                $"game:{gameId}:info",
                $"game:{gameId}:players",
                $"game:{gameId}:board",
                $"game:{gameId}:turn"
            };
            await _redis.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
        }
    }
}

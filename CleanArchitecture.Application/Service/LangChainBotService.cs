using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace CleanArchitecture.Application.Service
{
    public class LangChainBotService : IBotService
    {
        private readonly ISplendorService _splendorService;
        private readonly IGameStateStore _stateStore;
        private readonly IRedisMapper _redisMapper;
        private readonly ILogger<LangChainBotService> _logger;
        private readonly HttpClient _http;

        public const string BOT_PREFIX = "BOT_";

        public LangChainBotService(
            ISplendorService splendorService,
            IGameStateStore stateStore,
            IRedisMapper redisMapper,
            ILogger<LangChainBotService> logger,
            HttpClient http)
        {
            _splendorService = splendorService;
            _stateStore = stateStore;
            _redisMapper = redisMapper;
            _logger = logger;
            _http = http;
        }

        // =====================================================================
        // ENTRY POINT
        // =====================================================================
        public async Task TakeTurnAsync(string roomCode, int delayMs = 10000)
        {
            try
            {
                await Task.Delay(delayMs);

                // Verify vẫn đang là lượt bot
                var context = await _stateStore.LoadGameContext(roomCode);
                if (context == null) return;

                var turnSystem = new TurnSystem();

                var botId = turnSystem.GetCurrentPlayerId(context);
                if (!botId.StartsWith(BOT_PREFIX))
                {
                    _logger.LogWarning("[LangChainBot] Not bot turn — roomCode={RoomCode}", roomCode);
                    return;
                }

                // Build game state giống BroadcastGameState để gửi cho agent
                var redisState = await BuildGameStateAsync(roomCode);
                if (redisState == null)
                {
                    _logger.LogWarning("[LangChainBot] No Redis state — roomCode={RoomCode}", roomCode);
                    return;
                }

                // Gọi LangChain agent
                var decision = await CallAgentAsync(redisState);
                if (decision == null)
                {
                    _logger.LogWarning("[LangChainBot] Agent returned null — roomCode={RoomCode}", roomCode);
                    return;
                }

                _logger.LogInformation("[LangChainBot] {Action} — {Reasoning}", decision.Action, decision.Reasoning);

                await ExecuteDecisionAsync(roomCode, botId, decision);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LangChainBot] TakeTurnAsync failed — roomCode={RoomCode}", roomCode);
            }
        }

        // =====================================================================
        // BUILD GAME STATE — dùng redisMapper giống BroadcastGameState
        // =====================================================================
        private async Task<object?> BuildGameStateAsync(string roomCode)
        {
            var info = await _redisMapper.GetGameInfo(roomCode);
            var players = await _redisMapper.GetPlayers(roomCode);
            var board = await _redisMapper.GetBoard(roomCode);
            var turn = await _redisMapper.GetTurn(roomCode);
            var cardDecks = await _redisMapper.GetCardDecks(roomCode);

            if (info == null) return null;

            return new
            {
                info = JsonSerializer.Deserialize<object>(info),
                players = players.ToDictionary(
                    kv => kv.Key,
                    kv => JsonSerializer.Deserialize<object>(kv.Value)
                ),
                board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null,
                cardDecks = cardDecks != null ? JsonSerializer.Deserialize<object>(cardDecks) : null,
            };
        }

        // =====================================================================
        // GỌI LANGCHAIN SERVICE
        // =====================================================================
        private async Task<AgentDecision?> CallAgentAsync(object redisState)
        {
            var response = await _http.PostAsJsonAsync("/decide", new { gameState = redisState });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AgentDecision>();
        }

        // =====================================================================
        // EXECUTE — map AgentDecision → gọi đúng service method
        // Giữ nguyên các case NeedsDiscard, NeedsSelectNoble từ BotService gốc
        // =====================================================================
        private async Task ExecuteDecisionAsync(string roomCode, string botId, AgentDecision decision)
        {
            switch (decision.Action)
            {
                case "TAKE_GEMS":
                    var gems = decision.Payload
                        .GetProperty("gems")
                        .Deserialize<Dictionary<GemColor, int>>()!;

                    var collectResult = await _splendorService.CollectGemsAsync(roomCode, botId, gems);
                    if (!collectResult.Success)
                    {
                        _logger.LogError("[LangChainBot] CollectGems failed");
                        return;
                    }
                    // Xử lý discard nếu vượt 10 gems — dùng lại logic từ BotService
                    if (collectResult.NeedsDiscard)
                    {
                        var toDiscard = CalculateDiscardGems(collectResult.CurrentGems);
                        await _splendorService.DiscardGemsAsync(roomCode, botId, toDiscard);
                    }
                    break;

                case "PURCHASE_CARD":
                    var cardId = Guid.Parse(decision.Payload.GetProperty("cardId").GetString()!);

                    var purchaseResult = await _splendorService.PurchaseCardAsync(roomCode, botId, cardId);
                    if (!purchaseResult.Success)
                    {
                        _logger.LogError("[LangChainBot] PurchaseCard failed — cardId={CardId}", cardId);
                        return;
                    }
                    // Chọn noble gây thiệt hại nhất cho opponent nếu eligible
                    if (purchaseResult.NeedsSelectNoble && purchaseResult.EligibleNobles.Any())
                    {
                        var bestNoble = PickMostDamagingNoble(roomCode, purchaseResult.EligibleNobles);
                        await _splendorService.SelectNobleAsync(roomCode, botId, bestNoble);
                    }
                    break;

                case "RESERVE_CARD":
                    var reserveCardId = decision.Payload.TryGetProperty("cardId", out var cidEl)
                        ? Guid.Parse(cidEl.GetString()!)
                        : (Guid?)null;

                    var reserveResult = await _splendorService.ReserveCardAsync(roomCode, botId, reserveCardId);
                    if (!reserveResult.Success)
                    {
                        _logger.LogError("[LangChainBot] ReserveCard failed");
                        return;
                    }
                    if (reserveResult.NeedsDiscard)
                    {
                        var toDiscard = CalculateDiscardGems(reserveResult.CurrentGems);
                        await _splendorService.DiscardGemsAsync(roomCode, botId, toDiscard);
                    }
                    break;

                case "PASS_TURN":
                    _logger.LogWarning("[LangChainBot] Agent decided PASS_TURN");
                    await _splendorService.EndTurnAsync(roomCode, botId);
                    break;

                default:
                    _logger.LogWarning("[LangChainBot] Unknown action: {Action}", decision.Action);
                    await _splendorService.EndTurnAsync(roomCode, botId);
                    break;
            }
        }

        // =====================================================================
        // NOBLE: Chọn noble gây thiệt hại nhất — chặn opponent gần nhất
        // =====================================================================
        private Guid PickMostDamagingNoble(string roomCode, IEnumerable<Guid> eligibleNobles)
        {
            // TODO: load context nếu muốn tính toán opponent bonuses
            // Hiện tại: chọn noble đầu tiên — đủ dùng cho tutorial bot
            return eligibleNobles.First();
        }

        // =====================================================================
        // DISCARD: Copy từ BotService gốc — bỏ gem nhiều nhất, giữ Gold cuối
        // =====================================================================
        private Dictionary<GemColor, int> CalculateDiscardGems(Dictionary<GemColor, int> currentGems)
        {
            var toDiscard = new Dictionary<GemColor, int>();
            int discardCount = currentGems.Values.Sum() - 10;

            var sorted = currentGems
                .Where(kv => kv.Key != GemColor.Gold && kv.Value > 0)
                .OrderByDescending(kv => kv.Value);

            foreach (var kv in sorted)
            {
                if (discardCount <= 0) break;
                int discard = Math.Min(kv.Value, discardCount);
                toDiscard[kv.Key] = discard;
                discardCount -= discard;
            }

            if (discardCount > 0 && currentGems.GetValueOrDefault(GemColor.Gold, 0) > 0)
                toDiscard[GemColor.Gold] = discardCount;

            return toDiscard;
        }
    }

    // =====================================================================
    // Response từ LangChain Node.js service
    // =====================================================================
    record AgentDecision(string Action, System.Text.Json.JsonElement Payload, string Reasoning);
}
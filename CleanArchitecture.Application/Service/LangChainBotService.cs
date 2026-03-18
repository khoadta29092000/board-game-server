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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IGameNotifier _notifier;

        public const string BOT_PREFIX = "BOT_";

        public LangChainBotService(
            ISplendorService splendorService,
            IGameStateStore stateStore,
            IRedisMapper redisMapper,
            ILogger<LangChainBotService> logger,
            IHttpClientFactory httpClientFactory,
            IGameNotifier notifier)
        {
            _splendorService = splendorService;
            _stateStore = stateStore;
            _redisMapper = redisMapper;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _notifier = notifier;
        }

        public async Task TakeTurnAsync(string roomCode, int delayMs = 2000)
        {
            try
            {
                await Task.Delay(delayMs);

                var context = await _stateStore.LoadGameContext(roomCode);
                if (context == null) return;

                var turnSystem = new TurnSystem();
                var botId = turnSystem.GetCurrentPlayerId(context);
                if (!botId.StartsWith(BOT_PREFIX))
                {
                    _logger.LogWarning("[LangChainBot] Not bot turn — roomCode={RoomCode}", roomCode);
                    return;
                }

                var redisState = await BuildGameStateAsync(roomCode);
                if (redisState == null)
                {
                    _logger.LogWarning("[LangChainBot] No Redis state — roomCode={RoomCode}", roomCode);
                    return;
                }

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

        private async Task<AgentDecision?> CallAgentAsync(object redisState)
        {
            var http = _httpClientFactory.CreateClient("BotAgent");
            var response = await http.PostAsJsonAsync("/decide", new { gameState = redisState });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AgentDecision>();
        }

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
                    // Broadcast trước để user thấy bot đã lấy gem
                    await _notifier.BroadcastGameStateAsync(roomCode);

                    // NeedsDiscard → gọi lại agent để bot tự quyết định bỏ gem nào
                    if (collectResult.NeedsDiscard)
                    {
                        await _notifier.NotifyBotThinkingAsync(roomCode);
                        var discardState = await BuildGameStateAsync(roomCode);
                        var discardDecision = await CallAgentAsync(discardState!);
                        if (discardDecision?.Action == "DISCARD_GEMS")
                        {
                            var toDiscard = discardDecision.Payload
                                .GetProperty("gems")
                                .Deserialize<Dictionary<GemColor, int>>()!;
                            await _splendorService.DiscardGemsAsync(roomCode, botId, toDiscard);
                        }
                        else
                        {
                            // Fallback tự tính nếu agent không trả DISCARD_GEMS
                            var toDiscard = CalculateDiscardGems(collectResult.CurrentGems);
                            await _splendorService.DiscardGemsAsync(roomCode, botId, toDiscard);
                        }
                        await _notifier.BroadcastGameStateAsync(roomCode);
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

                    if (purchaseResult.IsGameOver)
                    {
                        await _notifier.NotifyGameOverAsync(roomCode, purchaseResult.Winner);
                        return;
                    }

                    // Broadcast trước để user thấy bot đã mua card
                    await _notifier.BroadcastGameStateAsync(roomCode);

                    // NeedsSelectNoble → gọi lại agent để bot chọn noble
                    if (purchaseResult.NeedsSelectNoble && purchaseResult.EligibleNobles.Any())
                    {
                        await _notifier.NotifyBotThinkingAsync(roomCode);
                        var nobleState = await BuildGameStateAsync(roomCode);
                        var nobleDecision = await CallAgentAsync(nobleState!);
                        if (nobleDecision?.Action == "SELECT_NOBLE")
                        {
                            var selectedNobleId = Guid.Parse(nobleDecision.Payload.GetProperty("nobleId").GetString()!); 
                            var selectedNoble = purchaseResult.EligibleNobles.Contains(selectedNobleId)
                                ? selectedNobleId
                                : purchaseResult.EligibleNobles.First();
                            await _splendorService.SelectNobleAsync(roomCode, botId, selectedNoble);
                        }
                        else
                        {
                            // Fallback chọn noble đầu tiên
                            await _splendorService.SelectNobleAsync(roomCode, botId, purchaseResult.EligibleNobles.First());
                        }
                        await _notifier.BroadcastGameStateAsync(roomCode);
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

                    // Broadcast trước để user thấy bot đã reserve
                    await _notifier.BroadcastGameStateAsync(roomCode);

                    // NeedsDiscard → gọi lại agent
                    if (reserveResult.NeedsDiscard)
                    {
                        await _notifier.NotifyBotThinkingAsync(roomCode);
                        var discardState = await BuildGameStateAsync(roomCode);
                        var discardDecision = await CallAgentAsync(discardState!);
                        if (discardDecision?.Action == "DISCARD_GEMS")
                        {
                            var toDiscard = discardDecision.Payload
                                .GetProperty("gems")
                                .Deserialize<Dictionary<GemColor, int>>()!;
                            await _splendorService.DiscardGemsAsync(roomCode, botId, toDiscard);
                        }
                        else
                        {
                            var toDiscard = CalculateDiscardGems(reserveResult.CurrentGems);
                            await _splendorService.DiscardGemsAsync(roomCode, botId, toDiscard);
                        }
                        await _notifier.BroadcastGameStateAsync(roomCode);
                    }
                    break;
                case "SELECT_NOBLE":
                    var nobleId = Guid.Parse(decision.Payload.GetProperty("nobleId").GetString()!);
                    var selectNobleResult = await _splendorService.SelectNobleAsync(roomCode, botId, nobleId);
                    if (selectNobleResult.IsGameOver)
                        await _notifier.NotifyGameOverAsync(roomCode, selectNobleResult.Winner);
                    else
                        await _notifier.BroadcastGameStateAsync(roomCode);
                    break;
                case "PASS_TURN":
                    _logger.LogWarning("[LangChainBot] Agent decided PASS_TURN");
                    await _splendorService.EndTurnAsync(roomCode, botId);
                    await _notifier.BroadcastGameStateAsync(roomCode);
                    break;

                default:
                    _logger.LogWarning("[LangChainBot] Unknown action: {Action}", decision.Action);
                    await _splendorService.EndTurnAsync(roomCode, botId);
                    await _notifier.BroadcastGameStateAsync(roomCode);
                    break;
            }
        }

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

    record AgentDecision(string Action, System.Text.Json.JsonElement Payload, string Reasoning);
}
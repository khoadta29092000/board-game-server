using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;

namespace CleanArchitecture.Application.Service
{
    public enum BotActionType
    {
        CollectGems,
        PurchaseCard,
        ReserveCard
    }

    public class BotAction
    {
        public BotActionType Type { get; set; }
        public Dictionary<GemColor, int>? Gems { get; set; }
        public Guid? CardId { get; set; }
    }

    public class CardInfo
    {
        public Guid Id { get; set; }
        public CardComponent Comp { get; set; } = null!;
    }

    public class BotService : IBotService
    {
        private readonly ISplendorService _splendorService;
        private readonly IGameStateStore _stateStore;

        public const string BOT_PLAYER_ID = "BOT_TUTORIAL";

        public BotService(ISplendorService splendorService, IGameStateStore stateStore)
        {
            _splendorService = splendorService;
            _stateStore = stateStore;
        }

        // =====================================================================
        // ENTRY POINT: Bot thực hiện 1 lượt hoàn chỉnh
        // =====================================================================
        public async Task TakeTurnAsync(string roomCode, int delayMs = 1500)
        {
            await Task.Delay(delayMs);

            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return;

            // Verify đúng lượt bot
            var turnSystem = new TurnSystem();
            var currentPlayerId = turnSystem.GetCurrentPlayerId(context);
            if (currentPlayerId != BOT_PLAYER_ID) return;

            var action = DecideAction(context);
            if (action == null) return;

            switch (action.Type)
            {
                case BotActionType.CollectGems:
                    var collectResult = await _splendorService.CollectGemsAsync(roomCode, BOT_PLAYER_ID, action.Gems!);

                    // Handle discard nếu bot lỡ > 10 gems
                    if (collectResult.NeedsDiscard)
                    {
                        var toDiscard = CalculateDiscardGems(collectResult.CurrentGems);
                        await _splendorService.DiscardGemsAsync(roomCode, BOT_PLAYER_ID, toDiscard);
                    }
                    break;

                case BotActionType.PurchaseCard:
                    var purchaseResult = await _splendorService.PurchaseCardAsync(roomCode, BOT_PLAYER_ID, action.CardId!.Value);

                    // Handle noble accidentally (bot không target noble nhưng có thể đạt được)
                    if (purchaseResult.NeedsSelectNoble && purchaseResult.EligibleNobles.Any())
                    {
                        await _splendorService.SelectNobleAsync(roomCode, BOT_PLAYER_ID, purchaseResult.EligibleNobles.First());
                    }
                    break;

                case BotActionType.ReserveCard:
                    var reserveResult = await _splendorService.ReserveCardAsync(roomCode, BOT_PLAYER_ID, action.CardId);

                    // Handle discard sau khi nhận gold từ reserve
                    if (reserveResult.NeedsDiscard)
                    {
                        var toDiscard = CalculateDiscardGems(reserveResult.CurrentGems);
                        await _splendorService.DiscardGemsAsync(roomCode, BOT_PLAYER_ID, toDiscard);
                    }
                    break;
            }
        }

        // =====================================================================
        // DECIDE ACTION: Greedy logic với Turn 1 đặc biệt
        // =====================================================================
        private BotAction? DecideAction(GameContext context)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var boardComp = boardEntity?.GetComponent<BoardComponent>();
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            if (boardComp == null || turnComp == null) return null;

            var botEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == BOT_PLAYER_ID);
            var botComp = botEntity?.GetComponent<PlayerComponent>();
            if (botComp == null) return null;

            // --- TURN 1 ĐẶC BIỆT ---
            // Bot lấy đúng 3 màu player đã đụng (< 4 trên board)
            // Giữ lại 2 màu còn = 4 để player có thể lấy ×2 ở turn 2
            if (turnComp.TurnNumber == 2) // TurnNumber == 2 vì player đi turn 1 trước, bot là turn 2
            {
                return DecideTurn1Action(boardComp);
            }

            // --- GREEDY LOGIC TỪ TURN 2+ ---
            return DecideGreedyAction(context, botComp, boardComp);
        }

        // =====================================================================
        // TURN 1 SPECIAL: Lấy 3 màu player đã đụng để giữ 2 màu =4 cho player
        // =====================================================================
        private BotAction DecideTurn1Action(BoardComponent boardComp)
        {
            // Màu nào player đã đụng = còn < 4 trên board
            var colorsTouchedByPlayer = boardComp.AvailableGems
                .Where(kv => kv.Key != GemColor.Gold && kv.Value < 4 && kv.Value > 0)
                .Select(kv => kv.Key)
                .Take(3)
                .ToList();

            // Nếu player lấy < 3 màu, fallback lấy thêm màu bất kỳ còn lại
            if (colorsTouchedByPlayer.Count < 3)
            {
                var extra = boardComp.AvailableGems
                    .Where(kv => kv.Key != GemColor.Gold
                              && !colorsTouchedByPlayer.Contains(kv.Key)
                              && kv.Value > 0)
                    .Select(kv => kv.Key)
                    .Take(3 - colorsTouchedByPlayer.Count);
                colorsTouchedByPlayer.AddRange(extra);
            }

            return new BotAction
            {
                Type = BotActionType.CollectGems,
                Gems = colorsTouchedByPlayer.ToDictionary(c => c, _ => 1)
            };
        }

        // =====================================================================
        // GREEDY LOGIC: Target card có điểm, mua lv1 hữu ích, lấy gem thiếu
        // =====================================================================
        private BotAction? DecideGreedyAction(GameContext context, PlayerComponent botComp, BoardComponent boardComp)
        {
            // 1. Tìm target card (lv2/lv3 có điểm cao, gần mua nhất)
            var targetCard = PickFinalTarget(context, botComp);

            // 2. Mua target ngay nếu đủ gem
            if (targetCard != null && CanAfford(targetCard.Comp, botComp))
            {
                return new BotAction { Type = BotActionType.PurchaseCard, CardId = targetCard.Id };
            }

            // 3. Tìm card lv1 bonus đúng màu target, mua được ngay
            if (targetCard != null)
            {
                var neededColors = GetGemShortfall(targetCard.Comp, botComp).Keys.ToHashSet();
                var usefulLv1 = GetAllVisibleCards(context, boardComp)
                    .Where(c => c.Comp.Level == 1
                             && neededColors.Contains(c.Comp.BonusColor)
                             && CanAfford(c.Comp, botComp))
                    .OrderByDescending(c => c.Comp.PrestigePoints)
                    .FirstOrDefault();

                if (usefulLv1 != null)
                {
                    return new BotAction { Type = BotActionType.PurchaseCard, CardId = usefulLv1.Id };
                }
            }

            // 4. Lấy gem còn thiếu cho target
            if (targetCard != null)
            {
                var gemsAction = PickGemsForTarget(targetCard.Comp, botComp, boardComp);
                if (gemsAction != null) return gemsAction;
            }

            // 5. Fallback: lấy 3 gem nhiều nhất trên board
            return FallbackCollectGems(boardComp);
        }

        // =====================================================================
        // PICK TARGET: Card có điểm, ưu tiên gần mua nhất
        // =====================================================================
        private CardInfo? PickFinalTarget(GameContext context, PlayerComponent botComp)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var boardComp = boardEntity?.GetComponent<BoardComponent>();
            if (boardComp == null) return null;

            return GetAllVisibleCards(context, boardComp)
                .Where(c => c.Comp.PrestigePoints > 0)
                .OrderBy(c =>
                {
                    var shortfall = GetGemShortfall(c.Comp, botComp).Values.Sum();
                    return shortfall * 10 - c.Comp.PrestigePoints;
                })
                .FirstOrDefault();
        }

        // =====================================================================
        // PICK GEMS FOR TARGET: Lấy gem màu còn thiếu
        // =====================================================================
        private BotAction? PickGemsForTarget(CardComponent target, PlayerComponent botComp, BoardComponent boardComp)
        {
            var shortfall = GetGemShortfall(target, botComp);

            var gemsToTake = shortfall
                .Where(kv => boardComp.AvailableGems.GetValueOrDefault(kv.Key, 0) > 0)
                .Select(kv => kv.Key)
                .Take(3)
                .ToList();

            if (!gemsToTake.Any()) return null;

            // Nếu chỉ thiếu 1 màu và còn ≥4 → lấy ×2
            if (gemsToTake.Count == 1 && boardComp.AvailableGems.GetValueOrDefault(gemsToTake[0], 0) >= 4)
            {
                return new BotAction
                {
                    Type = BotActionType.CollectGems,
                    Gems = new Dictionary<GemColor, int> { { gemsToTake[0], 2 } }
                };
            }

            return new BotAction
            {
                Type = BotActionType.CollectGems,
                Gems = gemsToTake.ToDictionary(c => c, _ => 1)
            };
        }

        // =====================================================================
        // FALLBACK: Lấy 3 gem màu nhiều nhất trên board
        // =====================================================================
        private BotAction FallbackCollectGems(BoardComponent boardComp)
        {
            var gems = boardComp.AvailableGems
                .Where(kv => kv.Key != GemColor.Gold && kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .ToDictionary(kv => kv.Key, _ => 1);

            return new BotAction { Type = BotActionType.CollectGems, Gems = gems };
        }

        // =====================================================================
        // DISCARD: Bỏ gem dư nhất so với target
        // =====================================================================
        private Dictionary<GemColor, int> CalculateDiscardGems(Dictionary<GemColor, int> currentGems)
        {
            var toDiscard = new Dictionary<GemColor, int>();
            int total = currentGems.Values.Sum();
            int discardCount = total - 10;

            // Bỏ gem Gold cuối cùng, bỏ gem nhiều nhất trước
            var sorted = currentGems
                .Where(kv => kv.Key != GemColor.Gold && kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .ToList();

            foreach (var kv in sorted)
            {
                if (discardCount <= 0) break;
                int discard = Math.Min(kv.Value, discardCount);
                toDiscard[kv.Key] = discard;
                discardCount -= discard;
            }

            // Nếu vẫn còn cần bỏ thêm → bỏ gold
            if (discardCount > 0 && currentGems.GetValueOrDefault(GemColor.Gold, 0) > 0)
            {
                toDiscard[GemColor.Gold] = discardCount;
            }

            return toDiscard;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================
        private List<CardInfo> GetAllVisibleCards(GameContext context, BoardComponent boardComp)
        {
            var result = new List<CardInfo>();
            foreach (var id in boardComp.VisibleCards.Values.SelectMany(list => list))
            {
                var comp = context.GetEntity<CardEntity>(id)?.GetComponent<CardComponent>();
                if (comp != null)
                    result.Add(new CardInfo { Id = id, Comp = comp });
            }
            return result;
        }

        private bool CanAfford(CardComponent card, PlayerComponent player)
        {
            return GetGemShortfall(card, player).Count == 0 ||
                   GetGemShortfall(card, player).Values.Sum() <= player.Gems.GetValueOrDefault(GemColor.Gold, 0);
        }

        private Dictionary<GemColor, int> GetGemShortfall(CardComponent card, PlayerComponent player)
        {
            var shortfall = new Dictionary<GemColor, int>();
            foreach (var cost in card.Cost)
            {
                if (cost.Key == GemColor.Gold) continue;
                int have = player.Gems.GetValueOrDefault(cost.Key, 0)
                         + player.Bonuses.GetValueOrDefault(cost.Key, 0);
                int missing = cost.Value - have;
                if (missing > 0) shortfall[cost.Key] = missing;
            }
            return shortfall;
        }
    }
}

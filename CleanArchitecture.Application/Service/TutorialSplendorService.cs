using Microsoft.Extensions.Logging;
using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.DTO.Splendor;
using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;

namespace CleanArchitecture.Application.Service
{
    public class TutorialSplendorService : ITutorialSplendorService
    {
        private readonly ISplendorService _splendorService;
        private readonly IGameStateStore _stateStore;
        private readonly IRedisMapper _redisMapper;
        private readonly ISplendorRepository _configRepo;
        private readonly ILogger<TutorialSplendorService> _logger;

        // =====================================================================
        // TUTORIAL BOARD: Card IDs từ MongoDB (cố định)
        // Visible lv1: c17(green/red×3), c9(blue/white×3), c25(red/green×3), c8(white/red×4 +1pt)
        // Visible lv2: c53(green/1pt), c59(red/1pt), c67(black/2pt), c62(red/2pt)
        // Visible lv3: c79(green/3pt), c83(red/3pt), c80(green/4pt), c84(red/4pt)
        // Nobles: n10(green×4,red×4), n3(blue×3,green×3,red×3), n6(red×4,black×4)
        // =====================================================================
        private static readonly HashSet<string> TutorialVisibleCardIds = new()
        {
            // Level 1
            "c8", "c4", "c11", "c3",
            // Level 2
            "c53", "c59", "c67", "c62",
            // Level 3
            "c79", "c83", "c80", "c84"
        };

        private static readonly HashSet<string> TutorialNobleIds = new() { "n10", "n3", "n6" };

        public TutorialSplendorService(
            ISplendorService splendorService,
            IGameStateStore stateStore,
            IRedisMapper redisMapper,
            ISplendorRepository configRepo,
            ILogger<TutorialSplendorService> logger)
        {
            _splendorService = splendorService;
            _stateStore = stateStore;
            _redisMapper = redisMapper;
            _configRepo = configRepo;
            _logger = logger;
        }

        // =====================================================================
        // START TUTORIAL: Build fixed board state, player đi trước, bot index 1
        // =====================================================================
        public async Task<GameContext> StartTutorialAsync(string playerId, string playerName)
        {
            var roomCode = $"TUTORIAL_{playerId}";

            // Load toàn bộ cards + nobles từ MongoDB
            var allCards = await _configRepo.LoadCardsAsync();
            var allNobles = await _configRepo.LoadNoblesAsync();

            var context = new GameContext();
            var session = new GameSession(roomCode);
            context.GameSession = session;

            // --- Player thật (index 0, đi trước) ---
            var playerEntity = new PlayerEntity(playerId, playerName);
            context.Entities[playerEntity.Id] = playerEntity;
            session.PlayerEntityIds.Add(playerEntity.Id);

            // --- Bot (index 1, đi sau) ---
            var botEntity = new PlayerEntity(BotService.BOT_PLAYER_ID, "Tutorial Bot");
            context.Entities[botEntity.Id] = botEntity;
            session.PlayerEntityIds.Add(botEntity.Id);

            // --- Board (2 players = 4 gems mỗi màu) ---
            var boardEntity = new BoardEntity(2);
            context.Entities[boardEntity.Id] = boardEntity;
            session.SetBoardEntityId(boardEntity.Id);
            var boardComp = boardEntity.GetComponent<BoardComponent>()!;

            // --- Load tất cả cards vào context ---
            // Map mongoId -> entity để tra cứu nhanh
            var mongoIdToEntity = new Dictionary<string, CardEntity>();
            foreach (var card in allCards)
            {
                context.Entities[card.Id] = card;
                session.CardDeckIds.Add(card.Id);

                // Lấy mongoId từ card — cần 1 field để map
                // Vì CardComponent không lưu mongoId, ta dùng thứ tự load
                // → Xem note bên dưới về MongoCardId
            }

            // --- Load tất cả nobles vào context ---
            foreach (var noble in allNobles)
            {
                context.Entities[noble.Id] = noble;
                session.NobleIds.Add(noble.Id);
            }

            // --- Build fixed board thay vì random ---
            BuildFixedBoard(context, boardComp, allCards, allNobles, session);

            session.StartGame();

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return context;
        }

        // =====================================================================
        // BUILD FIXED BOARD: Visible cards + nobles cố định, deck = phần còn lại
        // =====================================================================
        private void BuildFixedBoard(
            GameContext context,
            BoardComponent boardComp,
            List<CardEntity> allCards,
            List<NobleEntity> allNobles,
            GameSession session)
        {
            // Group cards theo level
            var cardsByLevel = allCards
                .GroupBy(c => c.GetComponent<CardComponent>()!.Level)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Với mỗi level: tách visible (cố định) vs deck (còn lại)
            foreach (var level in new[] { 1, 2, 3 })
            {
                if (!cardsByLevel.ContainsKey(level)) continue;

                var levelCards = cardsByLevel[level];

                // Visible: pick đúng 4 cards theo design
                var visibleForLevel = PickVisibleCards(level, levelCards, context);
                boardComp.VisibleCards[level] = visibleForLevel.Select(c => c.Id).ToList();

                // Deck: shuffle phần còn lại
                var visibleIds = visibleForLevel.Select(c => c.Id).ToHashSet();
                var deckCards = levelCards
                    .Where(c => !visibleIds.Contains(c.Id))
                    .OrderBy(_ => Guid.NewGuid()) // shuffle
                    .Select(c => c.Id)
                    .ToList();

                boardComp.CardDecks[level] = new Queue<Guid>(deckCards);
            }

            // Nobles cố định: n10, n3, n6
            var nobleMapping = BuildNobleMapping(allNobles);
            boardComp.VisibleNobles = TutorialNobleIds
                .Where(nid => nobleMapping.ContainsKey(nid))
                .Select(nid => nobleMapping[nid].Id)
                .ToList();

            // Update session.NobleIds chỉ giữ nobles visible + deck
            session.NobleIds.Clear();
            session.NobleIds.AddRange(boardComp.VisibleNobles);
        }

        // =====================================================================
        // PICK VISIBLE CARDS: Match theo design tutorial
        // Level 1: "c8", "c4", "c11", "c3",
        // Level 2: c53, c59, c67, c62
        // Level 3: c79, c83, c80, c84
        // =====================================================================
        private List<CardEntity> PickVisibleCards(int level, List<CardEntity> levelCards, GameContext context)
        {
            // Map cards theo đặc điểm để match với design
            // Vì không có mongoId trong CardComponent, match theo (bonusColor, cost pattern)
            var designByLevel = GetDesignCards(level);

            var result = new List<CardEntity>();
            var usedIds = new HashSet<Guid>();

            foreach (var design in designByLevel)
            {
                var match = levelCards
                    .Where(c => !usedIds.Contains(c.Id))
                    .Select(c => new { Entity = c, Comp = c.GetComponent<CardComponent>()! })
                    .FirstOrDefault(x =>
                        x.Comp.BonusColor == design.BonusColor &&
                        x.Comp.PrestigePoints == design.Points &&
                        CostMatches(x.Comp.Cost, design.Cost));

                if (match != null)
                {
                    result.Add(match.Entity);
                    usedIds.Add(match.Entity.Id);
                }
            }

            // Fallback: nếu không match đủ 4 → lấy thêm bất kỳ
            foreach (var card in levelCards)
            {
                if (result.Count >= 4) break;
                if (!usedIds.Contains(card.Id))
                {
                    result.Add(card);
                    usedIds.Add(card.Id);
                }
            }

            return result;
        }

        // =====================================================================
        // DESIGN CARDS: Đặc điểm từng card cố định theo MongoDB data
        //"c8", "c4", "c11", "c3",
        // =====================================================================
        private List<CardDesign> GetDesignCards(int level)
        {
            return level switch
            {
                1 => new List<CardDesign>
                {
                    // c4: blue bonus, 0pt, cost: white×3
                    new(GemColor.White, 0, new() { { GemColor.Blue, 1 }, { GemColor.Green, 2 },{ GemColor.Red, 1 },{ GemColor.Black, 1 }}),
                    // c11: red bonus, 0pt, cost: green×3
                    new(GemColor.Blue, 0, new() { { GemColor.White, 1 }, { GemColor.Green, 1 },{ GemColor.Red, 1 },{ GemColor.Black, 2 }  }),
                    // c3: white bonus, 0pt, cost: red×4
                    new(GemColor.White, 0, new() {{ GemColor.Blue, 1 }, { GemColor.Green, 1 },{ GemColor.Red, 1 },{ GemColor.Black, 2 } }),
                     // c8: green bonus, 1pt, cost: red×3
                    new(GemColor.White, 1, new() { { GemColor.Red, 4 } }),
                },
                2 => new List<CardDesign>
                {
                    // c53: green bonus, 1pt, cost: white×3,green×2,red×2
                    new(GemColor.Green, 1, new() { { GemColor.White, 3 }, { GemColor.Green, 2 }, { GemColor.Red, 2 } }),
                    // c59: red bonus, 1pt, cost: blue×2,green×3,red×2
                    new(GemColor.Red, 1, new() { { GemColor.Blue, 2 }, { GemColor.Green, 3 }, { GemColor.Red, 2 } }),
                    // c67: black bonus, 2pt, cost: green×5
                    new(GemColor.Black, 2, new() { { GemColor.Green, 5 } }),
                    // c62: red bonus, 2pt, cost: white×2,blue×2,red×3
                    new(GemColor.Red, 2, new() { { GemColor.White, 2 }, { GemColor.Blue, 2 }, { GemColor.Red, 3 } }),
                },
                3 => new List<CardDesign>
                {
                    // c79: green bonus, 3pt, cost: blue×5,green×3,red×3,black×3
                    new(GemColor.Green, 3, new() { { GemColor.Blue, 5 }, { GemColor.Green, 3 }, { GemColor.Red, 3 }, { GemColor.Black, 3 } }),
                    // c83: red bonus, 3pt, cost: white×3,green×3,red×3,black×5
                    new(GemColor.Red, 3, new() { { GemColor.White, 3 }, { GemColor.Green, 3 }, { GemColor.Red, 3 }, { GemColor.Black, 5 } }),
                    // c80: green bonus, 4pt, cost: white×7
                    new(GemColor.Green, 4, new() { { GemColor.White, 7 } }),
                    // c84: red bonus, 4pt, cost: blue×7
                    new(GemColor.Red, 4, new() { { GemColor.Blue, 7 } }),
                },
                _ => new List<CardDesign>()
            };
        }

        // =====================================================================
        // NOBLE MAPPING: Match noble theo requirements
        // =====================================================================
        private Dictionary<string, NobleEntity> BuildNobleMapping(List<NobleEntity> allNobles)
        {
            // Map theo requirements signature
            var nobleDesigns = new Dictionary<string, Dictionary<GemColor, int>>
            {
                // n10: green×4, red×4
                { "n10", new() { { GemColor.Green, 4 }, { GemColor.Red, 4 } } },
                // n3: blue×3, green×3, red×3
                { "n3", new() { { GemColor.Blue, 3 }, { GemColor.Green, 3 }, { GemColor.Red, 3 } } },
                // n6: red×4, black×4
                { "n6", new() { { GemColor.Red, 4 }, { GemColor.Black, 4 } } },
            };

            var result = new Dictionary<string, NobleEntity>();
            var usedIds = new HashSet<Guid>();

            foreach (var (nid, req) in nobleDesigns)
            {
                var match = allNobles.FirstOrDefault(n =>
                    !usedIds.Contains(n.Id) &&
                    NobleRequirementsMatch(n.GetComponent<NobleComponent>()!.Requirements, req));

                if (match != null)
                {
                    result[nid] = match;
                    usedIds.Add(match.Id);
                }
            }

            return result;
        }

        // =====================================================================
        // DELEGATE ACTIONS: Wrap thẳng SplendorService, không đổi gì
        // =====================================================================
        public async Task<CollectGemResult> CollectGemsAsync(string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                return await _splendorService.CollectGemsAsync(roomCode, playerId, gems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] CollectGemsAsync failed — playerId={P}", playerId);
                throw;
            }
        }

        public async Task<bool> DiscardGemsAsync(string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                return await _splendorService.DiscardGemsAsync(roomCode, playerId, gems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] DiscardGemsAsync failed — playerId={P}", playerId);
                throw;
            }
        }

        public async Task<PurchaseCardResult> PurchaseCardAsync(string playerId, Guid cardId)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                return await _splendorService.PurchaseCardAsync(roomCode, playerId, cardId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] PurchaseCardAsync failed — playerId={P}", playerId);
                throw;
            }
        }

        public async Task<SelectNobleResult> SelectNobleAsync(string playerId, Guid nobleId)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                return await _splendorService.SelectNobleAsync(roomCode, playerId, nobleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] SelectNobleAsync failed — playerId={P}", playerId);
                throw;
            }
        }

        public async Task<ReserveCardResult> ReserveCardAsync(string playerId, Guid? cardId, int? level = null)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                return await _splendorService.ReserveCardAsync(roomCode, playerId, cardId, level);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] ReserveCardAsync failed — playerId={P}", playerId);
                throw;
            }
        }

        public async Task<GameContext?> GetTutorialStateAsync(string playerId)
        {
            try
            {
                return await _stateStore.LoadGameContext(GetRoomCode(playerId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] GetTutorialStateAsync failed — playerId={P}", playerId);
                throw;
            }
        }
        public async Task<bool> PassTurnAsync(string playerId)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                var context = await _stateStore.LoadGameContext(roomCode);
                if (context == null) return false;

                var validationSystem = new ValidationSystem();
                if (!validationSystem.IsPlayerTurn(context, playerId)) return false;

                // Force phase = Completed → TurnSystem sẽ advance sang lượt tiếp
                var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
                var turnComp = boardEntity?.GetComponent<TurnComponent>();
                if (turnComp == null) return false;

                turnComp.Phase = TurnPhase.Completed;
                var turnSystem = new TurnSystem();
                turnSystem.Execute(context);

                await _stateStore.SaveGameContext(roomCode, context);
                await _redisMapper.SyncGameStateToRedis(context, roomCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] PassTurnAsync failed — playerId={P}", playerId);
                return false;
            }
        }

        // Tutorial không lưu Mongo, chỉ xóa Redis
        public async Task EndTutorialAsync(string playerId)
        {
            try
            {
                var roomCode = GetRoomCode(playerId);
                await _stateStore.DeleteGameContext(roomCode);
                await _redisMapper.DeleteGame(roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TutorialService] EndTutorialAsync failed — playerId={P}", playerId);
                throw;
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================
        public static string GetRoomCode(string playerId) => $"TUTORIAL_{playerId}";

        private bool CostMatches(Dictionary<GemColor, int> actual, Dictionary<GemColor, int> expected)
        {
            if (actual.Count != expected.Count) return false;
            return expected.All(kv => actual.TryGetValue(kv.Key, out int val) && val == kv.Value);
        }

        private bool NobleRequirementsMatch(Dictionary<GemColor, int> actual, Dictionary<GemColor, int> expected)
        {
            if (actual.Count != expected.Count) return false;
            return expected.All(kv => actual.TryGetValue(kv.Key, out int val) && val == kv.Value);
        }
    }

    // =====================================================================
    // HELPER CLASS: Card design để match với MongoDB data
    // =====================================================================
    public record CardDesign(
        GemColor BonusColor,
        int Points,
        Dictionary<GemColor, int> Cost
    );
}

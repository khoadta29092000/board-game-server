using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.DTO.Splendor;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.Model.Room;
using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;

namespace CleanArchitecture.Application.Service
{
    public class SplendorService : ISplendorService
    {
        private readonly ISplendorRepository _configRepo;
        private readonly IGameStateStore _stateStore;
        private readonly IRedisMapper _redisMapper;
        private readonly GameInitializationSystem _initSystem;
        private readonly GemCollectionSystem _gemSystem;
        private readonly CardPurchaseSystem _purchaseSystem;
        private readonly CardReservationSystem _reserveSystem;
        private readonly DiscardGemSystem _discardSystem;
        private readonly NobleVisitSystem _nobleSystem;
        private readonly TurnSystem _turnSystem;
        private readonly EndGameSystem _endGameSystem;

        public SplendorService(
            ISplendorRepository configRepo,
            IGameStateStore stateStore,
            IRedisMapper redisMapper,
            GameInitializationSystem initSystem,
            GemCollectionSystem gemSystem,
            CardPurchaseSystem purchaseSystem,
            CardReservationSystem reserveSystem,
            DiscardGemSystem discardSystem,
            NobleVisitSystem nobleSystem,
            TurnSystem turnSystem,
            EndGameSystem endGameSystem)
        {
            _configRepo = configRepo;
            _stateStore = stateStore;
            _redisMapper = redisMapper;
            _initSystem = initSystem;
            _gemSystem = gemSystem;
            _purchaseSystem = purchaseSystem;
            _reserveSystem = reserveSystem;
            _discardSystem = discardSystem;
            _nobleSystem = nobleSystem;
            _turnSystem = turnSystem;
            _endGameSystem = endGameSystem;
        }

        // =====================================================================
        // START GAME
        // =====================================================================
        public async Task<GameContext> StartGameAsync(string roomCode, List<RoomPlayer> playerIds)
        {
            var context = new GameContext();
            var session = new GameSession(roomCode);
            context.GameSession = session;

            foreach (var pid in playerIds)
            {
                var playerEntity = new PlayerEntity(pid.PlayerId, pid.Name);
                context.Entities[playerEntity.Id] = playerEntity;
                session.PlayerEntityIds.Add(playerEntity.Id);
            }

            var boardEntity = new BoardEntity(playerIds.Count);
            context.Entities[boardEntity.Id] = boardEntity;
            session.SetBoardEntityId(boardEntity.Id);

            var cards = await _configRepo.LoadCardsAsync();
            var nobles = await _configRepo.LoadNoblesAsync();

            foreach (var card in cards)
            {
                context.Entities[card.Id] = card;
                session.CardDeckIds.Add(card.Id);
            }

            foreach (var noble in nobles)
            {
                context.Entities[noble.Id] = noble;
                session.NobleIds.Add(noble.Id);
            }

            _initSystem.Execute(context);
            session.StartGame();

            await _stateStore.SaveGameContext(roomCode, context);

            return context;
        }

        // =====================================================================
        // GET GAME
        // =====================================================================
        public async Task<GameContext?> GetGameAsync(string roomCode)
        {
            return await _stateStore.LoadGameContext(roomCode);
        }

        // =====================================================================
        // FORCE START
        // =====================================================================
        public async Task<bool> ForceStartGameAsync(string roomCode)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            context.GameSession.StartGame();

            await _stateStore.SaveGameContext(roomCode, context);
            return true;
        }

        // =====================================================================
        // COLLECT GEMS
        // - Validate turn, phase, gem selection
        // - Nếu tổng gems > 10 sau khi nhận → cần discard, giữ phase SelectingGems
        // - Nếu ok → chuyển turn
        // =====================================================================
        public async Task<CollectGemResult> CollectGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems)
        {
            var context = await _stateStore.LoadGameContext(roomCode);

            if (context == null)
                throw new GameNotFoundException(roomCode);

            if (context.GameSession.Status != GameStatus.InProgress)
                throw new GameNotInProgressException(roomCode);

            var currentPlayer = _turnSystem.GetCurrentPlayerId(context);
            if (currentPlayer != playerId)
                throw new NotYourTurnException(playerId, currentPlayer);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp?.Phase != TurnPhase.WaitingForAction)
                throw new InvalidTurnPhaseException(turnComp?.Phase ?? TurnPhase.WaitingForAction, TurnPhase.WaitingForAction);

            if (!_gemSystem.CanCollectGems(context, playerId, gems))
                throw new InvalidGemCollectionException("Invalid gem selection or insufficient gems on board");

            _gemSystem.CollectGems(context, playerId, gems);

            var player = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId)
                ?.GetComponent<PlayerComponent>();

            int totalGems = player?.Gems.Values.Sum() ?? 0;
            bool needsDiscard = totalGems > 10;

            if (!needsDiscard && turnComp != null)
            {
                turnComp.Phase = TurnPhase.Completed;
                _turnSystem.Execute(context);
            }

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return new CollectGemResult
            {
                Success = true,
                NeedsDiscard = needsDiscard,
                TotalGems = totalGems,
                CurrentGems = player?.Gems ?? new Dictionary<GemColor, int>()
            };
        }

        // =====================================================================
        // DISCARD GEMS
        // - Chỉ gọi sau CollectGems khi tổng gems > 10
        // - Sau khi discard xong, DiscardGemSystem tự set phase = Completed nếu <= 10
        // - Hub sẽ broadcast lại state
        // =====================================================================
        public async Task<bool> DiscardGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_discardSystem.CanDiscardGem(context, playerId, gems))
                return false;

            _discardSystem.DiscardGem(context, playerId, gems);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp?.Phase == TurnPhase.Completed)
                _turnSystem.Execute(context);

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return true;
        }

        // =====================================================================
        // PURCHASE CARD
        // - Validate, mua card, trả gems về bank
        // - Check noble eligible: 1 → auto assign, nhiều → chờ chọn, 0 → chuyển turn
        // - Check end game sau khi phase = Completed
        // =====================================================================
        public async Task<PurchaseCardResult> PurchaseCardAsync(string roomCode, string playerId, Guid cardId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return new PurchaseCardResult { Success = false };

            if (!_purchaseSystem.CanPurchaseCard(context, playerId, cardId))
                return new PurchaseCardResult { Success = false };

            _purchaseSystem.PurchaseCard(context, playerId, cardId);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            var eligibleNobles = _nobleSystem.GetEligibleNobles(context, playerId);

            bool wasLastRound = turnComp?.IsLastRound ?? false;

            if (eligibleNobles.Count == 1)
            {
                _nobleSystem.AssignNoble(context, playerId, eligibleNobles[0]);
                if (turnComp != null)
                {
                    turnComp.Phase = TurnPhase.Completed;
                    _endGameSystem.Execute(context);
                    if (context.GameSession.Status != GameStatus.Completed)
                        _turnSystem.Execute(context);
                }
            }
            else if (eligibleNobles.Count > 1)
            {
                if (turnComp != null)
                    turnComp.Phase = TurnPhase.SelectingNoble;
                // Chưa check end game, chờ SelectNoble xong
            }
            else
            {
                if (turnComp != null)
                {
                    turnComp.Phase = TurnPhase.Completed;
                    _endGameSystem.Execute(context);
                    if (context.GameSession.Status != GameStatus.Completed)
                        _turnSystem.Execute(context);
                }
            }

            bool isGameOver = context.GameSession.Status == GameStatus.Completed;
            bool justTriggeredLastRound = !wasLastRound && (turnComp?.IsLastRound ?? false);

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return new PurchaseCardResult
            {
                Success = true,
                NeedsSelectNoble = eligibleNobles.Count > 1,
                EligibleNobles = eligibleNobles.Count > 1 ? eligibleNobles : new(),
                IsGameOver = isGameOver,
                JustTriggeredLastRound = justTriggeredLastRound,
                Winner = isGameOver ? context.GameSession.WinnerId : null
            };
        }

        // =====================================================================
        // SELECT NOBLE
        // - Chỉ gọi sau PurchaseCard khi có nhiều noble eligible
        // - Assign noble được chọn → chuyển turn → check end game
        // =====================================================================
        public async Task<SelectNobleResult> SelectNobleAsync(string roomCode, string playerId, Guid nobleId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return new SelectNobleResult { Success = false };

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp?.Phase != TurnPhase.SelectingNoble)
                throw new InvalidTurnPhaseException(turnComp?.Phase ?? TurnPhase.WaitingForAction, TurnPhase.SelectingNoble);

            var eligible = _nobleSystem.GetEligibleNobles(context, playerId);
            if (!eligible.Contains(nobleId))
                return new SelectNobleResult { Success = false };

            bool wasLastRound = turnComp.IsLastRound;

            _nobleSystem.AssignNoble(context, playerId, nobleId);
            turnComp.Phase = TurnPhase.Completed;

            _endGameSystem.Execute(context);

            bool isGameOver = context.GameSession.Status == GameStatus.Completed;
            bool justTriggeredLastRound = !wasLastRound && turnComp.IsLastRound;

            if (!isGameOver)
                _turnSystem.Execute(context);

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return new SelectNobleResult
            {
                Success = true,
                IsGameOver = isGameOver,
                JustTriggeredLastRound = justTriggeredLastRound,
                Winner = isGameOver ? context.GameSession.WinnerId : null
            };
        }

        // =====================================================================
        // RESERVE CARD
        // - Giữ card từ board hoặc rút blind từ deck theo level
        // - Nhận gold gem nếu bank còn
        // - Chuyển turn ngay, không có bước phụ
        // - Reserve không thể trigger win nên không cần check EndGame
        // =====================================================================
        public async Task<bool> ReserveCardAsync(string roomCode, string playerId, Guid cardId, int? level = null)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_reserveSystem.CanReserveCard(context, playerId, level, cardId))
                return false;

            _reserveSystem.ReserveCard(context, playerId, level, cardId);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp != null)
            {
                turnComp.Phase = TurnPhase.Completed;
                _turnSystem.Execute(context);
            }

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return true;
        }

        // =====================================================================
        // END TURN (manual fallback)
        // =====================================================================
        public async Task EndTurnAsync(string roomCode, string playerId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return;

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp != null)
                turnComp.Phase = TurnPhase.Completed;

            _turnSystem.Execute(context);
            await _stateStore.SaveGameContext(roomCode, context);
        }
    }
}
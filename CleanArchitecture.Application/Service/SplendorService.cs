using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
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

        // inject các System
        private readonly GameInitializationSystem _initSystem;
        private readonly GemCollectionSystem _gemSystem;
        private readonly CardPurchaseSystem _purchaseSystem;
        private readonly CardReservationSystem _reserveSystem;
        private readonly DiscardGemSystem _discardSystem;
        private readonly NobleVisitSystem _nobleSystem;
        private readonly TurnSystem _turnSystem;
        private readonly EndGameSystem _winSystem;

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
            EndGameSystem winSystem)
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
            _winSystem = winSystem;
        }

        public async Task<GameContext> StartGameAsync(string roomCode, List<RoomPlayer> playerIds)
        {
            var context = new GameContext();
            var session = new GameSession(roomCode);
            context.GameSession = session;

            // Add players
            foreach (var pid in playerIds)
            {
                var playerEntity = new PlayerEntity(pid.PlayerId, pid.Name);
                context.Entities[playerEntity.Id] = playerEntity;
                session.PlayerEntityIds.Add(playerEntity.Id);
            }

            // Create board
            var boardEntity = new BoardEntity(playerIds.Count);
            context.Entities[boardEntity.Id] = boardEntity;
            session.SetBoardEntityId(boardEntity.Id);

            // Load cards & nobles from Mongo
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
        public async Task<GameContext?> GetGameAsync(string roomCode)
        {
            return await _stateStore.LoadGameContext(roomCode);
        }
        public async Task<bool> ForceStartGameAsync(string roomCode)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            // Force update status
            context.GameSession.StartGame();

            await _stateStore.SaveGameContext(roomCode, context);
            return true;
        }
        public async Task<object> CollectGemsAsync(string gameId, string playerId, Dictionary<GemColor, int> gems)
        {
            var context = await _stateStore.LoadGameContext(gameId);

            if (context == null)
                throw new GameNotFoundException(gameId);

            if (context.GameSession.Status != GameStatus.InProgress)
                throw new GameNotInProgressException(gameId);

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

            if (turnComp != null && turnComp.Phase != TurnPhase.SelectingGems)
            {
                turnComp.Phase = TurnPhase.Completed;
                _turnSystem.Execute(context);
                _winSystem.Execute(context);
            }

            await _stateStore.SaveGameContext(gameId, context);
            await _redisMapper.SyncGameStateToRedis(context, gameId);
            return new
            {
                success = true,
                needsDiscard = turnComp?.Phase == TurnPhase.SelectingGems,
                currentGems = context.GameSession.PlayerEntityIds
                                     .Select(id => context.GetEntity<PlayerEntity>(id))
                                     .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId)
                                     ?.GetComponent<PlayerComponent>()?.Gems.Values.Sum()
            };
        }

        public async Task<bool> PurchaseCardAsync(string roomCode, string playerId, Guid cardId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_purchaseSystem.CanPurchaseCard(context, playerId, cardId))
                return false;

            _purchaseSystem.PurchaseCard(context, playerId, cardId);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            var eligibleNobles = _nobleSystem.GetEligibleNobles(context, playerId);

            if (eligibleNobles.Count == 1)
            {
                // Chỉ 1 noble → tự động assign và chuyển turn
                _nobleSystem.AssignNoble(context, playerId, eligibleNobles[0]);

                if (turnComp != null)
                {
                    turnComp.Phase = TurnPhase.Completed;
                    _turnSystem.Execute(context); // ← Chuyển turn
                }
            }
            else if (eligibleNobles.Count > 1)
            {
                // Nhiều nobles → player phải chọn
                if (turnComp != null)
                    turnComp.Phase = TurnPhase.SelectingNoble;
                // TODO: Lưu danh sách eligible nobles vào component
            }
            else
            {
                // Không có noble → chuyển turn luôn
                if (turnComp != null)
                {
                    turnComp.Phase = TurnPhase.Completed;
                    _turnSystem.Execute(context); // ← Chuyển turn
                }
            }

            _winSystem.Execute(context);

            // ✅ Lưu và sync
            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return true;
        }
        public async Task<bool> SelectNobleAsync(string roomCode, string playerId, Guid nobleId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            // Validate đang trong phase SelectingNoble
            if (turnComp?.Phase != TurnPhase.SelectingNoble)
                throw new InvalidTurnPhaseException(turnComp?.Phase ?? TurnPhase.WaitingForAction, TurnPhase.SelectingNoble);

            // Validate noble có trong danh sách eligible
            var eligible = _nobleSystem.GetEligibleNobles(context, playerId);
            if (!eligible.Contains(nobleId))
                return false;

            // Assign noble
            _nobleSystem.AssignNoble(context, playerId, nobleId);

            // Chuyển turn
            turnComp.Phase = TurnPhase.Completed;
            _turnSystem.Execute(context);

            // Check win condition
            _winSystem.Execute(context);

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return true;
        }

        public async Task<bool> ReserveCardAsync(string roomCode, string playerId, Guid cardId, int? level = null)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_reserveSystem.CanReserveCard(context, playerId, level, cardId))
                return false;

            _reserveSystem.ReserveCard(context, playerId, level, cardId);

            // ✅ Reserve card xong → chuyển turn luôn
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp != null)
            {
                turnComp.Phase = TurnPhase.Completed;
                _turnSystem.Execute(context); // ← Chuyển turn
                _winSystem.Execute(context);
            }

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return true;
        }

        public async Task<bool> DiscardGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_discardSystem.CanDiscardGem(context, playerId, gems))
                return false;

            _discardSystem.DiscardGem(context, playerId, gems);

            // ✅ DiscardGem đã set phase = Completed nếu <= 10 gems
            // Chỉ cần gọi Execute để chuyển turn
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            if (turnComp?.Phase == TurnPhase.Completed)
            {
                _turnSystem.Execute(context); // ← Chuyển turn
                _winSystem.Execute(context);
            }

            await _stateStore.SaveGameContext(roomCode, context);
            await _redisMapper.SyncGameStateToRedis(context, roomCode);

            return true;
        }

        public async Task EndTurnAsync(string roomCode, string playerId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return;

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComponent = boardEntity?.GetComponent<TurnComponent>();
            if (turnComponent != null)
            {
                turnComponent.Phase = TurnPhase.Completed;
            }

            _turnSystem.Execute(context);
            await _stateStore.SaveGameContext(roomCode, context);
        }
    }
}

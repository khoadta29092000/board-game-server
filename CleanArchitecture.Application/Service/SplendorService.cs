using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Room;
using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.Service
{
    public class SplendorService : ISplendorService
    {
        private readonly ISplendorRepository _configRepo;
        private readonly IGameStateStore _stateStore;

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

        public async Task<bool> CollectGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            // ✓ Validate game state
            if (context.GameSession.Status != GameStatus.InProgress)
                return false;

            // ✓ Validate player turn
            var currentPlayer = _turnSystem.GetCurrentPlayerId(context);
            if (currentPlayer != playerId)
                return false;

            // ✓ Validate turn phase
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            if (turnComp?.Phase != TurnPhase.WaitingForAction)
                return false;

            // Business logic
            if (!_gemSystem.CanCollectGems(context, playerId, gems)) return false;
            _gemSystem.CollectGems(context, playerId, gems);

            await _stateStore.SaveGameContext(roomCode, context);
            return true;
        }

        public async Task<bool> PurchaseCardAsync(string roomCode, string playerId, Guid cardId)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_purchaseSystem.CanPurchaseCard(context, playerId, cardId))
                return false;

            _purchaseSystem.PurchaseCard(context, playerId, cardId);

            // ✓ Lấy boardEntity từ context
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();

            var eligibleNobles = _nobleSystem.GetEligibleNobles(context, playerId);
            if (eligibleNobles.Count == 1)
            {
                _nobleSystem.AssignNoble(context, playerId, eligibleNobles[0]);
            }
            else if (eligibleNobles.Count > 1)
            {
                // Set phase để player chọn noble
                if (turnComp != null)
                {
                    turnComp.Phase = TurnPhase.SelectingNoble;
                    // TODO: Lưu danh sách eligible nobles để player chọn
                    // Có thể thêm vào TurnComponent hoặc PlayerComponent
                }
            }

            _winSystem.Execute(context);
            await _stateStore.SaveGameContext(roomCode, context);
            return true;
        }

        public async Task<bool> ReserveCardAsync(string roomCode, string playerId, Guid cardId, int? level = null)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_reserveSystem.CanReserveCard(context, playerId, level, cardId))
                return false;

            // ✓ Truyền đúng thứ tự: level, cardId
            _reserveSystem.ReserveCard(context, playerId, level, cardId);

            await _stateStore.SaveGameContext(roomCode, context);
            return true;
        }

        public async Task<bool> DiscardGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems)
        {
            var context = await _stateStore.LoadGameContext(roomCode);
            if (context == null) return false;

            if (!_discardSystem.CanDiscardGem(context, playerId, gems)) return false;
            _discardSystem.DiscardGem(context, playerId, gems);

            await _stateStore.SaveGameContext(roomCode, context);
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

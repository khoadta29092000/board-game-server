using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;


namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class ValidationSystem : ISystem
    {
        public void Execute(GameContext context) { }

        // Validate nếu đúng lượt của player
        public bool IsPlayerTurn(GameContext context, string playerId)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            if (turnComp == null) return false;

            var currentEntityId = context.GameSession.PlayerEntityIds[turnComp.CurrentPlayerIndex];
            var currentPlayer = context.GetEntity<PlayerEntity>(currentEntityId);
            var playerComp = currentPlayer?.GetComponent<PlayerComponent>();

            return playerComp?.PlayerId == playerId;
        }

        // Validate game state
        public bool IsGameInProgress(GameContext context)
        {
            return context.GameSession.Status == GameStatus.InProgress;
        }
    }
}

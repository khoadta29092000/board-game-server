using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;


namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class TurnSystem : ISystem
    {
        public void Execute(GameContext context)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComponent = boardEntity?.GetComponent<TurnComponent>();

            if (turnComponent == null) return;

            // Check if turn phase is completed
            if (turnComponent.Phase == TurnPhase.Completed)
            {
                NextTurn(context, turnComponent);
            }
        }

        private void NextTurn(GameContext context, TurnComponent turnComponent)
        {
            var playerCount = context.GameSession.PlayerEntityIds.Count;
            turnComponent.CurrentPlayerIndex = (turnComponent.CurrentPlayerIndex + 1) % playerCount;
            turnComponent.TurnNumber += 1;
            turnComponent.Phase = TurnPhase.WaitingForAction;
            turnComponent.LastActionTime = DateTime.UtcNow;
        }

        public string GetCurrentPlayerId(GameContext context)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComponent = boardEntity?.GetComponent<TurnComponent>();

            if (turnComponent == null) return string.Empty;

            var currentPlayerEntityId = context.GameSession.PlayerEntityIds[turnComponent.CurrentPlayerIndex];
            var playerEntity = context.GetEntity<PlayerEntity>(currentPlayerEntityId);
            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();

            return playerComponent?.PlayerId ?? string.Empty;
        }
    }
}

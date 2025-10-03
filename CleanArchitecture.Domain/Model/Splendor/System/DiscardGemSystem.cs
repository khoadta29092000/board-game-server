using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;


namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class DiscardGemSystem : ISystem
    {
        public void Execute(GameContext context) { /* no-op */ }

        public bool CanDiscardGem(GameContext context, string playerId, Dictionary<GemColor, int> toDiscard)
        {
            var playerEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();
            if (playerComponent == null) return false;

            foreach (var kv in toDiscard)
            {
                if (playerComponent.Gems.GetValueOrDefault(kv.Key, 0) < kv.Value)
                    return false;
            }
            return true;
        }

        public void DiscardGem(GameContext context, string playerId, Dictionary<GemColor, int> toDiscard)
        {
            var playerEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);

            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();
            var boardComponent = boardEntity?.GetComponent<BoardComponent>();

            if (playerComponent == null || boardComponent == null) return;

            foreach (var kv in toDiscard)
            {
                playerComponent.Gems[kv.Key] -= kv.Value;
                boardComponent.AvailableGems[kv.Key] += kv.Value;
            }

            // check sau discard
            if (playerComponent.Gems.Values.Sum() <= 10)
            {
                var turnComponent = boardEntity.GetComponent<TurnComponent>();
                if (turnComponent != null)
                    turnComponent.Phase = TurnPhase.Completed;
            }
        }
    }
}

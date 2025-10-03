using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;


namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class EndGameSystem : ISystem
    {
        private const int WinningPoints = 15;

        public void Execute(GameContext context)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            if (turnComp == null) return;

            bool hasPlayerWith15Points = CheckIfAnyPlayerHas15Points(context);

            if (hasPlayerWith15Points)
            {
                if (!turnComp.IsLastRound)
                {
                    turnComp.IsLastRound = true;
                    turnComp.LastRoundStartPlayerIndex = turnComp.CurrentPlayerIndex;
                    return;
                }

                bool isBackToStartPlayer = turnComp.CurrentPlayerIndex == turnComp.LastRoundStartPlayerIndex;
                bool hasTurnCompleted = turnComp.Phase == TurnPhase.Completed;

                if (isBackToStartPlayer && hasTurnCompleted)
                {
                    var winner = DetermineFinalWinner(context);

                    // SỬ DỤNG METHOD THAY VÌ SET TRỰC TIẾP
                    if (winner != null)
                    {
                        context.GameSession.CompleteGame(winner);
                    }
                }
            }
        }

        private bool CheckIfAnyPlayerHas15Points(GameContext context)
        {
            foreach (var playerEntityId in context.GameSession.PlayerEntityIds)
            {
                var playerEntity = context.GetEntity<PlayerEntity>(playerEntityId);
                var playerComp = playerEntity?.GetComponent<PlayerComponent>();

                if (playerComp != null && playerComp.PrestigePoints >= WinningPoints)
                {
                    return true;
                }
            }
            return false;
        }

        public string? DetermineFinalWinner(GameContext context)
        {
            var players = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id)?.GetComponent<PlayerComponent>())
                .Where(p => p != null)
                .OrderByDescending(p => p!.PrestigePoints)
                .ThenBy(p => CountPurchasedCards(context, p!.PlayerId))
                .ToList();

            return players.FirstOrDefault()?.PlayerId;
        }

        private int CountPurchasedCards(GameContext context, string playerId)
        {
            var player = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            return player?.GetComponent<PlayerComponent>()?.Bonuses.Values.Sum() ?? 0;
        }

        public List<string> GetPlayersWithMinPoints(GameContext context, int minPoints)
        {
            var result = new List<string>();

            foreach (var playerEntityId in context.GameSession.PlayerEntityIds)
            {
                var playerEntity = context.GetEntity<PlayerEntity>(playerEntityId);
                var playerComp = playerEntity?.GetComponent<PlayerComponent>();

                if (playerComp != null && playerComp.PrestigePoints >= minPoints)
                {
                    result.Add(playerComp.PlayerId);
                }
            }

            return result;
        }
    }
}

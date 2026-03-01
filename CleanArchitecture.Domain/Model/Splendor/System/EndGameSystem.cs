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
            if (!hasPlayerWith15Points) return;

            if (!turnComp.IsLastRound)
            {
                turnComp.IsLastRound = true;
                // CurrentPlayerIndex đã tăng sau TurnSystem
                // LastRoundStartPlayerIndex = index của player tiếp theo (người bắt đầu last round)
                turnComp.LastRoundStartPlayerIndex = turnComp.CurrentPlayerIndex;
                return;
            }

            // Game over khi quay lại đúng người bắt đầu last round
            bool isBackToStart = turnComp.CurrentPlayerIndex == turnComp.LastRoundStartPlayerIndex;
            if (isBackToStart)
            {
                var winner = DetermineFinalWinner(context);
                if (winner != null)
                    context.GameSession.CompleteGame(winner);
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

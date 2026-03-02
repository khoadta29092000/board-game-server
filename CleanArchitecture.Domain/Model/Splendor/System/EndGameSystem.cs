using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;


namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class EndGameSystem : ISystem
    {
        /// <summary>
        /// Logic last round Splendor:
        /// A(0)→B(1)→C(2)→A(0)→...
        ///
        /// Khi có người đạt 15+ điểm:
        /// - Trigger là A(0) → B, C đi nốt → end (A không đi thêm)
        /// - Trigger là B(1) → C đi nốt → end (A, B không đi thêm)
        /// - Trigger là C(2) → end ngay (C là người cuối vòng)
        ///
        /// Rule: end sau khi người CUỐI VÒNG (index = playerCount-1) đi xong
        /// = khi TurnSystem tăng index và CurrentPlayerIndex wrap về 0
        ///
        /// Ngoại lệ: trigger là người cuối vòng → end ngay không cần last round
        /// </summary>
        private const int WinningPoints = 15;

        public void Execute(GameContext context)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            if (turnComp == null) return;

            bool hasPlayerWith15Points = CheckIfAnyPlayerHas15Points(context);
            if (!hasPlayerWith15Points) return;

            var playerCount = context.GameSession.PlayerEntityIds.Count;

            if (!turnComp.IsLastRound)
            {
                // TurnSystem đã chạy trước → CurrentPlayerIndex đã tăng
                // Người vừa trigger = index trước đó
                int triggerIndex = (turnComp.CurrentPlayerIndex - 1 + playerCount) % playerCount;

                if (triggerIndex == playerCount - 1)
                {
                    // Trigger là người CUỐI VÒNG → end ngay
                    var winner = DetermineFinalWinner(context);
                    if (winner != null)
                        context.GameSession.CompleteGame(winner);
                    return;
                }

                // Trigger không phải người cuối → còn người sau cần đi
                // End khi người cuối vòng (playerCount-1) đi xong
                // = khi CurrentPlayerIndex wrap về 0
                turnComp.IsLastRound = true;
                return;
            }

            // Đang trong last round
            // CurrentPlayerIndex vừa được TurnSystem tăng
            // End khi index == 0 tức là người cuối vòng (playerCount-1) vừa đi xong
            if (turnComp.CurrentPlayerIndex == 0)
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
                    return true;
            }
            return false;
        }

        public string? DetermineFinalWinner(GameContext context)
        {
            var players = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id)?.GetComponent<PlayerComponent>())
                .Where(p => p != null)
                .OrderByDescending(p => p!.PrestigePoints)
                .ThenBy(p => p!.PurchaseCards.Count)
                .ToList();

            return players.FirstOrDefault()?.PlayerId;
        }

        public List<string> GetPlayersWithMinPoints(GameContext context, int minPoints)
        {
            var result = new List<string>();
            foreach (var playerEntityId in context.GameSession.PlayerEntityIds)
            {
                var playerEntity = context.GetEntity<PlayerEntity>(playerEntityId);
                var playerComp = playerEntity?.GetComponent<PlayerComponent>();
                if (playerComp != null && playerComp.PrestigePoints >= minPoints)
                    result.Add(playerComp.PlayerId);
            }
            return result;
        }
    }
}
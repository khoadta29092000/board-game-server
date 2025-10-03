using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class GemCollectionSystem : ISystem
    {
        private readonly DiscardGemSystem _discardSystem;
        public GemCollectionSystem(DiscardGemSystem discardSystem)
        {
            _discardSystem = discardSystem;
        }

        public void Execute(GameContext context) { /* no-op */ }

        public bool CanCollectGems(GameContext context, string playerId, Dictionary<GemColor, int> gemsToCollect)
        {
            var board = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId)?.GetComponent<BoardComponent>();
            if (board == null) return false;

            // Không được lấy vàng trực tiếp
            if (gemsToCollect.ContainsKey(GemColor.Gold)) return false;

            int total = gemsToCollect.Values.Sum();
            if (total > 3) return false;

            int distinct = gemsToCollect.Count(kv => kv.Value > 0);
            int maxSame = gemsToCollect.Where(kv => kv.Key != GemColor.Gold).Max(kv => kv.Value);

            if (total == 3)
            {
                // 3 màu khác nhau
                if (!(gemsToCollect.Values.All(v => v == 1) && distinct == 3)) return false;
            }
            else if (total == 2)
            {
                // 2 cùng màu, chỉ khi còn >=4 trên board
                if (maxSame != 2) return false;
                var color = gemsToCollect.First(kv => kv.Value == 2).Key;
                if (board.AvailableGems.GetValueOrDefault(color, 0) < 4) return false;
            }
            else return false;

            // Check availability
            foreach (var kv in gemsToCollect)
            {
                if (board.AvailableGems.GetValueOrDefault(kv.Key, 0) < kv.Value) return false;
            }

            return true;
        }

        public void CollectGems(GameContext context, string playerId, Dictionary<GemColor, int> gemsToCollect)
        {
            var player = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var playerComp = player?.GetComponent<PlayerComponent>();
            var boardComp = boardEntity?.GetComponent<BoardComponent>();
            if (playerComp == null || boardComp == null) return;

            // Move gems
            foreach (var kv in gemsToCollect)
            {
                playerComp.Gems[kv.Key] = playerComp.Gems.GetValueOrDefault(kv.Key, 0) + kv.Value;
                boardComp.AvailableGems[kv.Key] = boardComp.AvailableGems.GetValueOrDefault(kv.Key, 0) - kv.Value;
            }

            // Check max 10 gem
            if (playerComp.Gems.Values.Sum() > 10)
            {
                var turnComp = boardEntity.GetComponent<TurnComponent>();
                if (turnComp != null)
                {
                    turnComp.Phase = TurnPhase.SelectingGems; // chờ discard
                }
            }
        }
    }
}

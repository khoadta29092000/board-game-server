using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class NobleVisitSystem : ISystem
    {
        public void Execute(GameContext context)
        {
            // Check if any nobles should visit current player
        }

        public List<Guid> GetEligibleNobles(GameContext context, string playerId)
        {
            var playerEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();
            if (playerComponent == null) return new List<Guid>();

            var eligibleNobles = new List<Guid>();

            // Chỉ check nobles còn trên board (NobleIds đã loại những noble đã về rồi)
            foreach (var nobleId in context.GameSession.NobleIds)
            {
                var nobleEntity = context.GetEntity<NobleEntity>(nobleId);
                var nobleComponent = nobleEntity?.GetComponent<NobleComponent>();
                if (nobleComponent == null) continue;

                // Thêm check: noble phải còn trên VisibleNobles của board
                var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
                var boardComp = boardEntity?.GetComponent<BoardComponent>();
                if (boardComp != null && !boardComp.VisibleNobles.Contains(nobleId))
                    continue; 

                bool meetsRequirements = nobleComponent.Requirements.All(req =>
                    playerComponent.Bonuses.GetValueOrDefault(req.Key, 0) >= req.Value);

                if (meetsRequirements)
                    eligibleNobles.Add(nobleId);
            }

            return eligibleNobles;
        } 

        public void AssignNoble(GameContext context, string playerId, Guid nobleId)
        {
            var playerEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var nobleEntity = context.GetEntity<NobleEntity>(nobleId);
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);

            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();
            var nobleComponent = nobleEntity?.GetComponent<NobleComponent>();
            var boardComponent = boardEntity?.GetComponent<BoardComponent>();

            if (playerComponent == null || nobleComponent == null || boardComponent == null) return;

            playerComponent.PrestigePoints += nobleComponent.PrestigePoints;
            boardComponent.VisibleNobles.Remove(nobleId);
            context.GameSession.NobleIds.Remove(nobleId);
        }
    }
}

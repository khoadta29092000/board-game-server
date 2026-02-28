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
    public class CardPurchaseSystem : ISystem
    {
        private readonly NobleVisitSystem _nobleVisitSystem;
        public CardPurchaseSystem(NobleVisitSystem nobleVisitSystem)
        {
            _nobleVisitSystem = nobleVisitSystem;
        }

        public void Execute(GameContext context)
        {
            // no-op, use PurchaseCard()
        }

        public bool CanPurchaseCard(GameContext context, string playerId, Guid cardId)
        {
            var playerEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var cardEntity = context.GetEntity<CardEntity>(cardId);
            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();
            var cardComponent = cardEntity?.GetComponent<CardComponent>();

            if (playerComponent == null || cardComponent == null) return false;

            int goldNeeded = 0;
            foreach (var kv in cardComponent.Cost)
            {
                if (kv.Key == GemColor.Gold) continue;
                var have = playerComponent.Bonuses.GetValueOrDefault(kv.Key, 0) + playerComponent.Gems.GetValueOrDefault(kv.Key, 0);
                if (have < kv.Value) goldNeeded += kv.Value - have;
            }

            return goldNeeded <= playerComponent.Gems.GetValueOrDefault(GemColor.Gold, 0);
        }

        public void PurchaseCard(GameContext context, string playerId, Guid cardId)
        {
            var playerEntity = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var cardEntity = context.GetEntity<CardEntity>(cardId);
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);

            var playerComponent = playerEntity?.GetComponent<PlayerComponent>();
            var cardComponent = cardEntity?.GetComponent<CardComponent>();
            var boardComponent = boardEntity?.GetComponent<BoardComponent>();

            if (playerComponent == null || cardComponent == null || boardComponent == null) return;

            // Determine level of the card so we can refill after removal
            var cardLevel = cardComponent.Level;

            // Pay cost: use bonuses first, then gems, then gold
            foreach (var cost in cardComponent.Cost)
            {
                if (cost.Key == GemColor.Gold) continue;

                int needed = cost.Value;
                int fromBonus = Math.Min(needed, playerComponent.Bonuses.GetValueOrDefault(cost.Key, 0));
                needed -= fromBonus;

                int fromGems = Math.Min(needed, playerComponent.Gems.GetValueOrDefault(cost.Key, 0));
                needed -= fromGems;

                // use gold for remaining needed
                if (needed > 0)
                {
                    var goldHave = playerComponent.Gems.GetValueOrDefault(GemColor.Gold, 0);
                    var useGold = Math.Min(goldHave, needed);
                    playerComponent.Gems[GemColor.Gold] = goldHave - useGold;
                    boardComponent.AvailableGems[GemColor.Gold] = boardComponent.AvailableGems.GetValueOrDefault(GemColor.Gold, 0) + useGold;
                    needed -= useGold;
                }


                // subtract used gems from player and add back to board
                playerComponent.Gems[cost.Key] = playerComponent.Gems.GetValueOrDefault(cost.Key, 0) - fromGems;
                boardComponent.AvailableGems[cost.Key] = boardComponent.AvailableGems.GetValueOrDefault(cost.Key, 0) + fromGems;
            }

            // Add card benefits
            playerComponent.PrestigePoints += cardComponent.PrestigePoints;
            playerComponent.Bonuses[cardComponent.BonusColor] = playerComponent.Bonuses.GetValueOrDefault(cardComponent.BonusColor, 0) + 1;
            playerComponent.PurchaseCards.Add(cardId);

            // Remove card from visible list (if exists)
            if (playerComponent.ReservedCards.Contains(cardId))
            {
                playerComponent.ReservedCards.Remove(cardId);
                // Không cần refill visible vì card không phải từ board
            }
            else
            {
                foreach (var level in boardComponent.VisibleCards.Keys)
                {
                    if (boardComponent.VisibleCards[level].Remove(cardId))
                    {
                        // refill from same level
                        var newCard = boardComponent.DrawFromDeck(level);
                        if (newCard != Guid.Empty)
                        {
                            boardComponent.VisibleCards[level].Add(newCard);
                        }
                        break;
                    }
                }
            }
            // After purchase, check nobles for this player
            var eligible = _nobleVisitSystem.GetEligibleNobles(context, playerId);
            if (eligible.Any())
            {
                // auto assign first eligible (or your policy)
                _nobleVisitSystem.AssignNoble(context, playerId, eligible.First());
            }
        }

    }
}

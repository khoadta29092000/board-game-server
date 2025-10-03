using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;


namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class CardReservationSystem : ISystem
    {
        public void Execute(GameContext context) { /* no-op */ }

        public bool CanReserveCard(GameContext context, string playerId, int? level = null, Guid? visibleCardId = null)
        {
            var player = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);
            var board = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            if (player == null || board == null) return false;

            var playerComp = player.GetComponent<PlayerComponent>();
            if (playerComp.ReservedCards.Count >= 3) return false;

            // if visibleCardId provided, ensure it exists in some visible
            if (visibleCardId.HasValue)
            {
                var exists = board.GetComponent<BoardComponent>().VisibleCards.Any(kv => kv.Value.Contains(visibleCardId.Value));
                if (!exists) return false;
            }
            else
            {
                // if reserving top of deck, ensure level provided and deck not empty
                if (!level.HasValue) return false;
                var deck = board.GetComponent<BoardComponent>().CardDecks[level.Value];
                if (deck.Count == 0) return false;
            }
            return true;
        }

        public void ReserveCard(GameContext context, string playerId, int? level = null, Guid? visibleCardId = null)
        {
            var player = context.GameSession.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id))
                .FirstOrDefault(p => p?.GetComponent<PlayerComponent>()?.PlayerId == playerId);

            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var playerComponent = player?.GetComponent<PlayerComponent>();
            var boardComponent = boardEntity?.GetComponent<BoardComponent>();

            if (playerComponent == null || boardComponent == null) return;

            Guid reservedId = Guid.Empty;

            if (visibleCardId.HasValue)
            {
                reservedId = visibleCardId.Value;
                // remove from visible
                foreach (var lvl in boardComponent.VisibleCards.Keys)
                {
                    if (boardComponent.VisibleCards[lvl].Remove(reservedId))
                    {
                        // refill same level
                        var newCard = boardComponent.DrawFromDeck(lvl);
                        if (newCard != Guid.Empty)
                        {
                            boardComponent.VisibleCards[lvl].Add(newCard);
                        }
                        break;
                    }
                }
            }
            else if (level.HasValue)
            {
                // draw top card from deck of that level to reserve
                var card = boardComponent.DrawFromDeck(level.Value);
                if (card == Guid.Empty) return;
                reservedId = card;
            }

            if (reservedId == Guid.Empty) return;

            playerComponent.ReservedCards.Add(reservedId);

            // give gold if available
            if (boardComponent.AvailableGems.GetValueOrDefault(GemColor.Gold, 0) > 0)
            {
                playerComponent.Gems[GemColor.Gold] = playerComponent.Gems.GetValueOrDefault(GemColor.Gold, 0) + 1;
                boardComponent.AvailableGems[GemColor.Gold] = boardComponent.AvailableGems.GetValueOrDefault(GemColor.Gold, 0) - 1;
            }
        }
    }
}

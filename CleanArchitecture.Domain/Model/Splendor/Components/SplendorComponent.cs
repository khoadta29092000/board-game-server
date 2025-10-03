using CleanArchitecture.Domain.Model.Splendor.Enum;

namespace CleanArchitecture.Domain.Model.Splendor.Components
{
    public interface IComponent { }

    // Component cho Player
    public class PlayerComponent : IComponent
    {
        public string PlayerId { get; set; }
        public string Name { get; set; }
        public int PrestigePoints { get; set; }
        public Dictionary<GemColor, int> Gems { get; set; }
        public Dictionary<GemColor, int> Bonuses { get; set; }
        public List<Guid> ReservedCards { get; set; }

        public PlayerComponent(string playerId, string name)
        {
            PlayerId = playerId;
            Name = name;
            PrestigePoints = 0;
            Gems = new Dictionary<GemColor, int>
            {
                { GemColor.White, 0 },
                { GemColor.Blue, 0 },
                { GemColor.Green, 0 },
                { GemColor.Red, 0 },
                { GemColor.Black, 0 },
                { GemColor.Gold, 0 }
            };
            Bonuses = new Dictionary<GemColor, int>
            {
                { GemColor.White, 0 },
                { GemColor.Blue, 0 },
                { GemColor.Green, 0 },
                { GemColor.Red, 0 },
                { GemColor.Black, 0 }
            };
            ReservedCards = new List<Guid>();
        }
    }

    // Component cho Card
    public class CardComponent : IComponent
    {
        public int Level { get; set; }
        public int PrestigePoints { get; set; }
        public GemColor BonusColor { get; set; }
        public Dictionary<GemColor, int> Cost { get; set; }

        public CardComponent(int level, int prestigePoints, GemColor bonusColor, Dictionary<GemColor, int> cost)
        {
            Level = level;
            PrestigePoints = prestigePoints;
            BonusColor = bonusColor;
            Cost = cost;
        }
    }

    // Component cho Noble
    public class NobleComponent : IComponent
    {
        public int PrestigePoints { get; set; }
        public Dictionary<GemColor, int> Requirements { get; set; }

        public NobleComponent(Dictionary<GemColor, int> requirements)
        {
            PrestigePoints = 3;
            Requirements = requirements;
        }
    }

    // Component cho Game Board
    public class BoardComponent : IComponent
    {
        public Dictionary<GemColor, int> AvailableGems { get; set; }
        public Dictionary<int, Queue<Guid>> CardDecks { get; set; }

        public Dictionary<int, List<Guid>> VisibleCards { get; set; }

        public List<Guid> VisibleNobles { get; set; }

        public BoardComponent(int playerCount)
        {
            AvailableGems = InitializeGems(playerCount);
            CardDecks = new Dictionary<int, Queue<Guid>>()
            {
                {1, new Queue<Guid>()},
                {2, new Queue<Guid>()},
                {3, new Queue<Guid>()}
            };
            VisibleCards = new Dictionary<int, List<Guid>>()
            {
                {1, new List<Guid>()},
                {2, new List<Guid>()},
                {3, new List<Guid>()}
            };
            VisibleNobles = new List<Guid>();
        }

        private Dictionary<GemColor, int> InitializeGems(int playerCount)
        {
            int regularGems = playerCount switch
            {
                2 => 4,
                3 => 5,
                4 => 7,
                _ => 7
            };

            return new Dictionary<GemColor, int>
            {
                { GemColor.White, regularGems },
                { GemColor.Blue, regularGems },
                { GemColor.Green, regularGems },
                { GemColor.Red, regularGems },
                { GemColor.Black, regularGems },
                { GemColor.Gold, 5 }
            };
        }

        // helper: refill visible up to 4 per level using deck
        public void RefillVisible(int level, int maxVisible = 4)
        {
            if (!CardDecks.ContainsKey(level) || !VisibleCards.ContainsKey(level)) return;

            var deck = CardDecks[level];
            var visible = VisibleCards[level];

            while (visible.Count < maxVisible && deck.Count > 0)
            {
                visible.Add(deck.Dequeue());
            }
        }

        // draw one from deck level (returns Guid.Empty if none)
        public Guid DrawFromDeck(int level)
        {
            if (!CardDecks.ContainsKey(level)) return Guid.Empty;
            var deck = CardDecks[level];
            if (deck.Count == 0) return Guid.Empty;
            return deck.Dequeue();
        }

        // shuffle utils (fill CardDecks from list per level)
        public void SeedDecks(Dictionary<int, List<Guid>> levelToCards, Random rng = null)
        {
            rng ??= new Random();
            foreach (var kv in levelToCards)
            {
                var level = kv.Key;
                var list = kv.Value.OrderBy(_ => rng.Next()).ToList();
                CardDecks[level] = new Queue<Guid>(list);
            }
        }
    }

    // Component cho Turn
    public class TurnComponent : IComponent
    {
        public int CurrentPlayerIndex { get; set; }
        public TurnPhase Phase { get; set; }
        public DateTime LastActionTime { get; set; }
        public bool IsLastRound { get; set; } 
        public int LastRoundStartPlayerIndex { get; set; }
        public TurnComponent()
        {
            CurrentPlayerIndex = 0;
            Phase = TurnPhase.WaitingForAction;
            LastActionTime = DateTime.UtcNow;
            IsLastRound = false;
            LastRoundStartPlayerIndex = -1;
        }
    }
}

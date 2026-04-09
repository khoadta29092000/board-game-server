using CleanArchitecture.Domain.Model.Splendor.Enum;
using System.Text.Json.Serialization;

namespace CleanArchitecture.Domain.Model.Splendor.Components
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$componentType")]
    [JsonDerivedType(typeof(PlayerComponent), "player")]
    [JsonDerivedType(typeof(CardComponent), "card")]
    [JsonDerivedType(typeof(NobleComponent), "noble")]
    [JsonDerivedType(typeof(BoardComponent), "board")]
    [JsonDerivedType(typeof(TurnComponent), "turn")]
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
        public List<Guid> PurchaseCards { get; set; }
        public string imageUrl { get; set; } = string.Empty;

        // ⬇️ THÊM constructor rỗng
        [JsonConstructor]
        public PlayerComponent()
        {
            PlayerId = string.Empty;
            Name = string.Empty;
            PrestigePoints = 0;
            Gems = new Dictionary<GemColor, int>();
            Bonuses = new Dictionary<GemColor, int>();
            ReservedCards = new List<Guid>();
            PurchaseCards = new List<Guid>();
            imageUrl = string.Empty;
        }

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
            PurchaseCards = new List<Guid>();
        }
    }

    // Component cho Card
    public class CardComponent : IComponent
    {
        public int Level { get; set; }
        public int PrestigePoints { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public GemColor BonusColor { get; set; } 
        public Dictionary<GemColor, int> Cost { get; set; }
        

        // ⬇️ THÊM constructor rỗng
        [JsonConstructor]
        public CardComponent()
        {
            Level = 0;
            PrestigePoints = 0;
            BonusColor = GemColor.White;
            Cost = new Dictionary<GemColor, int>();
            ImageUrl = string.Empty;
        }

        public CardComponent(int level, int prestigePoints, GemColor bonusColor, Dictionary<GemColor, int> cost, string imageUrl)
        {
            Level = level;
            PrestigePoints = prestigePoints;
            BonusColor = bonusColor;
            Cost = cost;
            ImageUrl = imageUrl;
        }
    }

    // Component cho Noble
    public class NobleComponent : IComponent
    {
        public int PrestigePoints { get; set; }
        public Dictionary<GemColor, int> Requirements { get; set; }
        public string ImageUrl { get; set; } = string.Empty;

        // ⬇️ THÊM constructor rỗng
        [JsonConstructor]
        public NobleComponent()
        {
            PrestigePoints = 3;
            Requirements = new Dictionary<GemColor, int>();
            ImageUrl = string.Empty;
        }

        public NobleComponent(Dictionary<GemColor, int> requirements, string imageUrl) 
        {
            PrestigePoints = 3;
            Requirements = requirements;
            ImageUrl = imageUrl;
        }
    }

    // Component cho Game Board
    public class BoardComponent : IComponent
    {
        public Dictionary<GemColor, int> AvailableGems { get; set; }
        public Dictionary<int, Queue<Guid>> CardDecks { get; set; }
        public Dictionary<int, List<Guid>> VisibleCards { get; set; }
        public List<Guid> VisibleNobles { get; set; }

        // ⬇️ THÊM constructor rỗng
        [JsonConstructor]
        public BoardComponent()
        {
            AvailableGems = new Dictionary<GemColor, int>();
            CardDecks = new Dictionary<int, Queue<Guid>>();
            VisibleCards = new Dictionary<int, List<Guid>>();
            VisibleNobles = new List<Guid>();
        }

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

        // ... rest of methods giữ nguyên
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

        public Guid DrawFromDeck(int level)
        {
            if (!CardDecks.ContainsKey(level)) return Guid.Empty;
            var deck = CardDecks[level];
            if (deck.Count == 0) return Guid.Empty;
            return deck.Dequeue();
        }

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
        
        public int TurnNumber { get; set; }

        public TurnComponent()
        {
            CurrentPlayerIndex = 0;
            Phase = TurnPhase.WaitingForAction;
            LastActionTime = DateTime.UtcNow;
            IsLastRound = false;
            LastRoundStartPlayerIndex = -1;
            TurnNumber = 1;
        }
    }
}
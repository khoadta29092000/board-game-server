using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using System.Text.Json.Serialization;

namespace CleanArchitecture.Domain.Model.Splendor.Entity
{
    public class GameSession : SplendorEntities
    {
        [JsonInclude]
        public GameStatus Status { get; private set; }
        [JsonInclude]
        public string? WinnerId { get; private set; }
        [JsonInclude]
        public DateTime? CompletedAt { get; private set; }
        [JsonInclude]
        public int? FirstPlayerToReach15Index { get; private set; }
        [JsonInclude]
        public string RoomCode { get; private set; }
        [JsonInclude]
        public DateTime CreatedAt { get; private set; }
        [JsonInclude]
        public DateTime? StartedAt { get; private set; }
        [JsonInclude]
        public List<Guid> PlayerEntityIds { get; private set; }
        [JsonInclude]
        public List<Guid> CardDeckIds { get; private set; }
        [JsonInclude]
        public List<Guid> NobleIds { get; private set; }   // ⬅ private set
        [JsonInclude]
        public Guid BoardEntityId { get; private set; }

        public GameSession(string roomCode)
        {
            RoomCode = roomCode;
            CreatedAt = DateTime.UtcNow;
            Status = GameStatus.Pending; 
            PlayerEntityIds = new List<Guid>();
            CardDeckIds = new List<Guid>();
            NobleIds = new List<Guid>();
        }

        public void SetBoardEntityId(Guid boardId) => BoardEntityId = boardId;
        public void AddPlayer(Guid playerId) => PlayerEntityIds.Add(playerId);
        public void StartGame()
        {
            Status = GameStatus.InProgress;
            StartedAt = DateTime.UtcNow;
        }

        public void CompleteGame(string winnerId)
        {
            Status = GameStatus.Completed;
            WinnerId = winnerId;
            CompletedAt = DateTime.UtcNow;
        }

    }

    public class PlayerEntity : SplendorEntities
    {
        public PlayerEntity() { }
        public PlayerEntity(string playerId, string name)
        {
            AddComponent(new PlayerComponent(playerId, name));
        }
    }

    public class CardEntity : SplendorEntities
    {
        public CardEntity() { }
        public CardEntity(int level, int prestigePoints, GemColor bonusColor, Dictionary<GemColor, int> cost, string imageUrl) { 
            AddComponent(new CardComponent(level, prestigePoints, bonusColor, cost, imageUrl));
        }
    }

    public class NobleEntity : SplendorEntities
    {
    public NobleEntity() { }
    public NobleEntity(Dictionary<GemColor, int> requirements, string imageUrl) 
    {
        AddComponent(new NobleComponent(requirements, imageUrl)); 
    }
}

    public class BoardEntity : SplendorEntities
    {
        public BoardEntity() { }
        public BoardEntity(int playerCount)
        {
            AddComponent(new BoardComponent(playerCount));
            AddComponent(new TurnComponent());
        }
    }
    }

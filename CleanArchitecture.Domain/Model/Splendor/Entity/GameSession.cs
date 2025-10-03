using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using static GraphQL.Validation.BasicVisitor;

namespace CleanArchitecture.Domain.Model.Splendor.Entity
{
    public class GameSession : SplendorEntities
    {
        public GameStatus Status { get; private set; }
        public string? WinnerId { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public int? FirstPlayerToReach15Index { get; private set; }
        public string RoomCode { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? StartedAt { get; private set; }
        public List<Guid> PlayerEntityIds { get; private set; }
        public List<Guid> CardDeckIds { get; private set; }
        public List<Guid> NobleIds { get; private set; }   // ⬅ private set
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
        public PlayerEntity(string playerId, string name)
        {
            AddComponent(new PlayerComponent(playerId, name));
        }
    }

    public class CardEntity : SplendorEntities
    {
        public CardEntity(int level, int prestigePoints, GemColor bonusColor, Dictionary<GemColor, int> cost)
        {
            AddComponent(new CardComponent(level, prestigePoints, bonusColor, cost));
        }
    }

    public class NobleEntity : SplendorEntities
    {
        public NobleEntity(Dictionary<GemColor, int> requirements)
        {
            AddComponent(new NobleComponent(requirements));
        }
    }

    public class BoardEntity : SplendorEntities
    {
        public BoardEntity(int playerCount)
        {
            AddComponent(new BoardComponent(playerCount));
            AddComponent(new TurnComponent());
        }
    }
    }

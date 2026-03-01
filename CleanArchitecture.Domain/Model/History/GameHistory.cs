using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.History
{
    public class GameHistory
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("gameId")]
        public string GameId { get; set; }

        [BsonElement("gameName")]
        public string GameName { get; set; }

        [BsonElement("state")]
        public string State { get; set; } = "Completed";

        [BsonElement("winnerId")]
        public string? WinnerId { get; set; }

        [BsonElement("winnerName")]
        public string? WinnerName { get; set; }

        [BsonElement("playerOrder")]
        public List<string> PlayerOrder { get; set; } = new();

        [BsonElement("players")]
        public Dictionary<string, GamePlayerInfo> Players { get; set; } = new();

        [BsonElement("totalTurns")]
        public int TotalTurns { get; set; }

        [BsonElement("durationSeconds")]
        public int DurationSeconds { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("startedAt")]
        public DateTime? StartedAt { get; set; }

        [BsonElement("completedAt")]
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Data đặc thù của từng game — BsonDocument lưu JSON linh hoạt.
        /// Splendor: gems bank còn lại, deck sizes...
        /// Game khác tự định nghĩa schema riêng mà không cần sửa class này.
        /// </summary>
        [BsonElement("gameData")]
        public BsonDocument GameData { get; set; } = new();
    }

    public class GamePlayerInfo
    {
        [BsonElement("playerId")]
        public string PlayerId { get; set; } = string.Empty;

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("rank")]
        public int Rank { get; set; }

        [BsonElement("score")]
        public int Score { get; set; }

        [BsonElement("isWinner")]
        public bool IsWinner { get; set; }

        /// <summary>
        /// Stats đặc thù của player — BsonDocument linh hoạt theo từng game.
        /// </summary>
        [BsonElement("stats")]
        public BsonDocument Stats { get; set; } = new();
    }
}

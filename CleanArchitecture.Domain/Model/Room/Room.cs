using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace CleanArchitecture.Domain.Model.Room
{
    public class Room
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonElement("roomId")]
        public string RoomId { get; set; }

        [BsonElement("roomType")]
        [BsonRepresentation(BsonType.String)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoomType RoomType { get; set; } = RoomType.Public;

        [BsonElement("quantityPlayer")]
        public int QuantityPlayer { get; set; }

        [BsonElement("currentPlayers")]
        public int CurrentPlayers { get; set; }

        [BsonElement("players")]
        public List<RoomPlayer> Players { get; set; } = new();

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
    }
}
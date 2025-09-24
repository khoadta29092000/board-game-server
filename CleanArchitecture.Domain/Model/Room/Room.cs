using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CleanArchitecture.Domain.Model.Room
{
    public class Room
    {
        [BsonElement("roomId")]
        public string Id { get; set; }

        [BsonElement("roomType")]
        public RoomType RoomType { get; set; } = RoomType.Public;

        [BsonElement("quantityPlayer")]
        public int QuantityPlayer { get; set; }

        [BsonElement("currentPlayers")]
        public int CurrentPlayers { get; set; }

        [BsonElement("players")]
        public List<RoomPlayer> Players { get; set; } = new();

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
    }
}
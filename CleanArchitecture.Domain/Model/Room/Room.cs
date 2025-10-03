using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using MessagePack;

namespace CleanArchitecture.Domain.Model.Room
{
    [MessagePackObject]
    public class Room
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [Key("id")]
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [BsonElement("roomId")]
        [Key("roomId")]
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; }

        [BsonElement("roomType")]
        [BsonRepresentation(BsonType.String)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Key("roomType")]
        [JsonPropertyName("roomType")]
        public RoomType RoomType { get; set; } = RoomType.Public;

        [BsonElement("quantityPlayer")]
        [Key("quantityPlayer")]
        [JsonPropertyName("quantityPlayer")]
        public int QuantityPlayer { get; set; }

        [BsonElement("currentPlayers")]
        [Key("currentPlayers")]
        [JsonPropertyName("currentPlayers")]
        public int CurrentPlayers { get; set; }

        [BsonElement("players")]
        [Key("players")]
        [JsonPropertyName("players")]
        public List<RoomPlayer> Players { get; set; } = new();

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Key("status")]
        [JsonPropertyName("status")]
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
    }
}
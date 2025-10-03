using MessagePack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace CleanArchitecture.Domain.Model.Room
{
    [MessagePackObject]
    public class RoomPlayer
    {
        [BsonElement("playerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [Key("playerId")]
        [JsonPropertyName("playerId")]
        public string PlayerId { get; set; }

        [BsonElement("name")]
        [Key("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [BsonElement("isOwner")]
        [Key("isOwner")]
        [JsonPropertyName("isOwner")]
        public bool IsOwner { get; set; }
        [BsonElement("isReady")]
        [Key("isReady")]
        [JsonPropertyName("isReady")]
        public bool isReady { get; set; }
    }
}
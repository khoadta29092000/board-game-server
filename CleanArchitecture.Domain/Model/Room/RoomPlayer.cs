using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CleanArchitecture.Domain.Model.Room
{
    public class RoomPlayer
    {
        [BsonElement("playerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string PlayerId { get; set; }

        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("isOwner")]
        public bool IsOwner { get; set; }
    }
}
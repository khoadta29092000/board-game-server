using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;


namespace CleanArchitecture.Domain.Model.Player
{
    public class Player
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonElement("name")]
        public string Name { get; set; }
        [BsonElement("username")]
        public string Username { get; set; }
        [BsonElement("hashedPassword")]
        public string? HashedPassword { get; set; }
        [BsonElement("saltPassword")]
        public string? SaltPassword { get; set; }

        [BsonElement("isVerified")]
        public bool IsVerified { get; set; } = false;
        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;
    }
}

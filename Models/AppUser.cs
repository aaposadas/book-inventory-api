using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BookInventory.Api.Models
{
    public class AppUser
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("Email")]
        [BsonRequired]
        public string Email { get; set; } = string.Empty;

        [BsonElement("FirstName")]
        [BsonRequired]
        public string FirstName { get; set; } = string.Empty;

        [BsonElement("LastName")]
        [BsonRequired]
        public string LastName { get; set; } = string.Empty;

        [BsonElement("Password")]
        [BsonRequired]
        public string Password { get; set; } = string.Empty;

        [BsonElement("CreatedAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
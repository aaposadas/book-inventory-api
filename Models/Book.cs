namespace BookInventory.Api.Models
{
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization.Attributes;

    public class Book
    {
        [BsonId] // Tells Mongo this is the _id field
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonElement("_id")] // Explicitly map to Mongo’s _id
        public string? Id { get; set; }

        [BsonElement("userId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;
        [BsonElement("ISBN")]
        public string? ISBN { get; set; }

        [BsonElement("title")]
        public string Title { get; set; } = string.Empty;

        [BsonElement("author")]
        public string Author { get; set; } = string.Empty;

        [BsonElement("publishedDate")]
        public string? PublishedDate { get; set; }

        [BsonElement("description")]
        public string? Description { get; set; }

        [BsonElement("categories")]
        public required List<string> Categories { get; set; }

        [BsonElement("coverUrl")]
        public string? CoverUrl { get; set; }

        [BsonElement("createdAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using BookInventory.Api.Models;

namespace BookInventory.Api.Data
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _database;

        public MongoDbContext(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            _database = mongoClient.GetDatabase(settings.Value.DatabaseName);
        }

        public IMongoCollection<Book> Books => _database.GetCollection<Book>("Books");
        public IMongoCollection<AppUser> Users => _database.GetCollection<AppUser>("Users");
    }
}

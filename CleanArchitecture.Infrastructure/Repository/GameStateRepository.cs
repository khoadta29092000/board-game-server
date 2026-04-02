using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.Model;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CleanArchitecture.Infrastructure.Repository
{
   
        public class GameStateRepository : IGameStateRepository
    {
            private readonly IMongoCollection<BsonDocument> _gameStates;

            public GameStateRepository(IOptions<DatabaseSettings> dbSettings)
            {
                var client = new MongoClient(dbSettings.Value.ConnectionString);
                var db = client.GetDatabase(dbSettings.Value.DatabaseName);
                _gameStates = db.GetCollection<BsonDocument>(
                    dbSettings.Value.GameStatesCollectionName);
            }

        public async Task<Dictionary<string, List<int>>> GetAvailableGameNamesAsync()
        {
            var docs = await _gameStates
                .Find(new BsonDocument())
                .Project(Builders<BsonDocument>.Projection
                    .Include("name")
                    .Include("playerOptions")
                    .Exclude("_id"))
                .ToListAsync();

            return docs
                .Where(d => d.Contains("name"))
                .ToDictionary(
                    d => d["name"].AsString,
                    d => d.Contains("playerOptions")
                        ? d["playerOptions"].AsBsonArray.Select(v => v.AsInt32).ToList()
                        : new List<int> { 2, 3, 4 } // fallback nếu chưa có field
                );
        }

        public async Task<bool> GameExistsAsync(string gameName)
            {
                var count = await _gameStates.CountDocumentsAsync(
                    new BsonDocument { { "name", gameName } });
                return count > 0;
            }
        }
}

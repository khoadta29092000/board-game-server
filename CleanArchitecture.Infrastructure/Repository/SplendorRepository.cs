using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Application.IRepository;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;


namespace CleanArchitecture.Infrastructure.Repository
{
    public class SplendorRepository : ISplendorRepository
    {
        private readonly IMongoCollection<BsonDocument> _games;

        public SplendorRepository(IOptions<DatabaseSettings> dbSettings)
        {
            var mongoClient = new MongoClient(dbSettings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(dbSettings.Value.DatabaseName);

            _games = mongoDatabase.GetCollection<BsonDocument>(dbSettings.Value.GameStatesCollectionName);
        }

        public async Task<List<CardEntity>> LoadCardsAsync()
        {
            var gameDoc = await _games.Find(new BsonDocument { { "name", "Splendor" } })
                                      .FirstOrDefaultAsync();

            var cards = new List<CardEntity>();
            foreach (var c in gameDoc["cards"].AsBsonArray)
            {
                var doc = c.AsBsonDocument;
                var level = doc["level"].AsInt32;
                var prestige = doc["points"].AsInt32;
                var bonus = (GemColor)Enum.Parse(typeof(GemColor), doc["bonusColor"].AsString, true);

                var cost = doc["cost"].AsBsonDocument.ToDictionary(
                    kv => (GemColor)Enum.Parse(typeof(GemColor), kv.Name, true),
                    kv => kv.Value.AsInt32
                );

                cards.Add(new CardEntity(level, prestige, bonus, cost));
            }

            return cards;
        }

        public async Task<List<NobleEntity>> LoadNoblesAsync()
        {
            var gameDoc = await _games.Find(new BsonDocument { { "name", "Splendor" } })
                                      .FirstOrDefaultAsync();

            var nobles = new List<NobleEntity>();
            foreach (var n in gameDoc["nobles"].AsBsonArray)
            {
                var nobleDoc = n.AsBsonDocument;
                var reqDoc = nobleDoc["requirements"].AsBsonDocument;
                var reqs = reqDoc.ToDictionary(
                    kv => (GemColor)Enum.Parse(typeof(GemColor), kv.Name, true),
                    kv => kv.Value.AsInt32
                );

                nobles.Add(new NobleEntity(reqs));
            }

            return nobles;
        }
    }
}

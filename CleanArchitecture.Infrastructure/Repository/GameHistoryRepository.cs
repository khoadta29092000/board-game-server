using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.History;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Repository
{
    public class GameHistoryRepository : IGameHistoryRepository
    {
        private readonly IMongoCollection<GameHistory> _collection;

        public GameHistoryRepository(IOptions<DatabaseSettings> settings)
        {
            if (settings == null || settings.Value == null)
                throw new ArgumentNullException(nameof(settings), "Database settings are null.");

            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);

            _collection = mongoDatabase.GetCollection<GameHistory>(
                settings.Value.GameHistoriesCollectionName);

            CreateIndexes();
        }

        private void CreateIndexes()
        {
            _collection.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<GameHistory>(
                    Builders<GameHistory>.IndexKeys.Ascending(x => x.GameId)),
                new CreateIndexModel<GameHistory>(
                    Builders<GameHistory>.IndexKeys.Ascending(x => x.GameName)),
                new CreateIndexModel<GameHistory>(
                    Builders<GameHistory>.IndexKeys
                        .Ascending(x => x.GameName)
                        .Descending(x => x.CompletedAt)),
                new CreateIndexModel<GameHistory>(
                    Builders<GameHistory>.IndexKeys
                        .Ascending(x => x.WinnerId)
                        .Ascending(x => x.GameName)),
            });
        }

        public async Task SaveAsync(GameHistory history)
        {
            try
            {
                await _collection.InsertOneAsync(history);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<GameHistory?> GetByGameIdAsync(string gameId) =>
            await _collection.Find(x => x.GameId == gameId).FirstOrDefaultAsync();

        public async Task<List<GameHistory>> GetByGameNameAsync(string gameName, int skip = 0, int limit = 20) =>
            await _collection
                .Find(x => x.GameName == gameName)
                .SortByDescending(x => x.CompletedAt)
                .Skip(skip)
                .Limit(limit)
                .ToListAsync();

        public async Task<List<GameHistory>> GetByPlayerIdAsync(string playerId, string? gameName = null, int limit = 20)
        {
            var filter = Builders<GameHistory>.Filter.Exists($"players.{playerId}", true);

            if (!string.IsNullOrEmpty(gameName))
                filter &= Builders<GameHistory>.Filter.Eq(x => x.GameName, gameName);

            return await _collection
                .Find(filter)
                .SortByDescending(x => x.CompletedAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<List<string>> GetGameNamesAsync() =>
            await _collection
                .Distinct<string>("gameName", FilterDefinition<GameHistory>.Empty)
                .ToListAsync();
    }
}

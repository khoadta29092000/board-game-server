using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.Room;
using CleanArchitecture.Domain.Model.VerificationCode;
using CleanArchitecture.Infrastructure.Security;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Repository
{
    public class RoomRepository : IRoomRepository
    {
        private readonly IMongoCollection<Room> _roomsCollection;

        public RoomRepository(IOptions<DatabaseSettings> dbSettings)
        {
            if (dbSettings == null || dbSettings.Value == null)
            {
                throw new ArgumentNullException(nameof(dbSettings), "Database settings are null.");
            }

            var mongoClient = new MongoClient(dbSettings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(dbSettings.Value.DatabaseName);

            _roomsCollection = mongoDatabase.GetCollection<Room>(
                dbSettings.Value.RoomsCollectionName);
        }

        public async Task<List<Room>> GetActiveRoom()
        {
            var filter = Builders<Room>.Filter.And(
                Builders<Room>.Filter.Eq(r => r.Status, RoomStatus.Waiting),
                Builders<Room>.Filter.Eq(r => r.RoomType, RoomType.Public)
            );

            return await _roomsCollection.Find(filter).ToListAsync();
            //return await _roomsCollection.Find(_ => true).ToListAsync();
        }
        public async Task<Room?> GetRoomById(string roomID)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomID);
            return await _roomsCollection.Find(filter).FirstOrDefaultAsync();
        }


        public async Task CreateRoom(Room room)
        {
            try
            {
                await _roomsCollection.InsertOneAsync(room);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task JoinRoom(string roomId, string playerId, string playerName)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var update = Builders<Room>.Update.Push(r => r.Players, new RoomPlayer
            {
                PlayerId = playerId,
                Name = playerName,
                IsOwner = false,
            });

            await _roomsCollection.UpdateOneAsync(filter, update);

        }

        public async Task LeaveRoom(string roomId, string playerId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);

            var update = Builders<Room>.Update
                .PullFilter(r => r.Players, p => p.PlayerId == playerId)
                .Inc(r => r.CurrentPlayers, -1);

            await _roomsCollection.UpdateOneAsync(filter, update);
        }

        public async Task StartGame(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);

            var update = Builders<Room>.Update
                .Set(r => r.Status, RoomStatus.Playing);

            await _roomsCollection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateRoomSettings(string roomId, int? maxPlayers = null, RoomType? roomType = null )
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);

            var updateDef = new List<UpdateDefinition<Room>>();

            if (maxPlayers.HasValue)
            {
                updateDef.Add(Builders<Room>.Update.Set(r => r.QuantityPlayer, maxPlayers.Value));
            }

            if (roomType.HasValue)
            {
                updateDef.Add(Builders<Room>.Update.Set(r => r.RoomType, roomType.Value));
            }

            if (updateDef.Count > 0)
            {
                var update = Builders<Room>.Update.Combine(updateDef);
                await _roomsCollection.UpdateOneAsync(filter, update);
            }
        }
    }
}

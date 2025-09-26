using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.Room;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

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
            var filter = Builders<Room>.Filter.And(Builders<Room>.Filter.Eq(r => r.Status, RoomStatus.Waiting),
                                                   Builders<Room>.Filter.Eq(r => r.RoomType, RoomType.Public));
            return await _roomsCollection.Find(filter).ToListAsync();
        }

        public async Task<Room?> GetRoomById(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            return await _roomsCollection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<Room> CreateRoom(Room room)
        {
            try
            {
                await _roomsCollection.InsertOneAsync(room);
                return room;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create room: {ex.Message}");
            }
        }

        public async Task<Room?> JoinRoom(string roomId, string playerId, string playerName)
        {
            var currentRoom = await GetRoomById(roomId);
            if (currentRoom == null || currentRoom.CurrentPlayers >= currentRoom.QuantityPlayer)
                return null;

            var filter = Builders<Room>.Filter.And(
                Builders<Room>.Filter.Eq(r => r.Id, roomId),
                Builders<Room>.Filter.Eq(r => r.Status, RoomStatus.Waiting),
                Builders<Room>.Filter.Lt(r => r.CurrentPlayers, currentRoom.QuantityPlayer), 
                Builders<Room>.Filter.Not(
                    Builders<Room>.Filter.ElemMatch(r => r.Players, p => p.PlayerId == playerId)
                )
            );

            var update = Builders<Room>.Update.Combine(
                Builders<Room>.Update.Push(r => r.Players, new RoomPlayer
                {
                    PlayerId = playerId,
                    Name = playerName,
                    IsOwner = false,
                }),
                Builders<Room>.Update.Inc(r => r.CurrentPlayers, 1)
            );

            var updatedRoom = await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room> { ReturnDocument = ReturnDocument.After }
            );

            return updatedRoom;
        }

        public async Task<Room?> LeaveRoom(string roomId, string playerId)
        {
            var room = await GetRoomById(roomId);
            if (room == null)
                return null;

            var playerToRemove = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
            if (playerToRemove == null)
                return room;

            if (room.CurrentPlayers <= 1)
            {
                await DeleteRoom(roomId);
                return null;
            }

            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var updateBuilder = Builders<Room>.Update
                .PullFilter(r => r.Players, p => p.PlayerId == playerId)
                .Inc(r => r.CurrentPlayers, -1);

            if (playerToRemove.IsOwner && room.Players.Count > 1)
            {
                var nextOwner = room.Players.FirstOrDefault(p => p.PlayerId != playerId);
                if (nextOwner != null)
                {
                    await _roomsCollection.UpdateOneAsync(filter, updateBuilder);

                    var ownerFilter = Builders<Room>.Filter.And(
                        Builders<Room>.Filter.Eq(r => r.Id, roomId),
                        Builders<Room>.Filter.ElemMatch(r => r.Players, p => p.PlayerId == nextOwner.PlayerId)
                    );
                    var ownerUpdate = Builders<Room>.Update.Set("Players.$.IsOwner", true);
                    await _roomsCollection.UpdateOneAsync(ownerFilter, ownerUpdate);
                }
            }
            else
            {
                await _roomsCollection.UpdateOneAsync(filter, updateBuilder);
            }

            return await GetRoomById(roomId);
        }

        public async Task<bool> DeleteRoom(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var result = await _roomsCollection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        public async Task<Room?> StartGame(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var update = Builders<Room>.Update.Set(r => r.Status, RoomStatus.Playing);

            var options = new FindOneAndUpdateOptions<Room>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await _roomsCollection.FindOneAndUpdateAsync(filter, update, options);
        }

        public async Task<Room?> UpdateRoomSettings(string roomId, int? maxPlayers, RoomType? roomType)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var updateList = new List<UpdateDefinition<Room>>();

            if (maxPlayers.HasValue)
                updateList.Add(Builders<Room>.Update.Set(r => r.QuantityPlayer, maxPlayers.Value));

            if (roomType.HasValue)
                updateList.Add(Builders<Room>.Update.Set(r => r.RoomType, roomType.Value));

            if (updateList.Count == 0)
                return await GetRoomById(roomId);

            var update = Builders<Room>.Update.Combine(updateList);
            var options = new FindOneAndUpdateOptions<Room>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await _roomsCollection.FindOneAndUpdateAsync(filter, update, options);
        }

        public async Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var update = Builders<Room>.Update.Set(r => r.Status, status);

            var options = new FindOneAndUpdateOptions<Room>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await _roomsCollection.FindOneAndUpdateAsync(filter, update, options);
        }
    }
}
using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.Room;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Numerics;

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

        public async Task<List<Room>> GetActiveRooms()
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Status, RoomStatus.Waiting);
            return await _roomsCollection.Find(filter).ToListAsync();
        }

        public async Task<Room?> GetRoomById(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            return await _roomsCollection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<Room> CreateRoom(Room room)
        {
            await _roomsCollection.InsertOneAsync(room);
            return room;
        }

        public async Task<Room> AddBotToRoom(string roomId, string botId, string botName)
        {
            var currentRoom = await GetRoomById(roomId);

            Console.WriteLine($"[AddBot] roomId={roomId}");
            Console.WriteLine($"[AddBot] currentRoom null={currentRoom == null}");
            Console.WriteLine($"[AddBot] status={currentRoom?.Status}");
            Console.WriteLine($"[AddBot] currentPlayers={currentRoom?.CurrentPlayers}");
            Console.WriteLine($"[AddBot] quantityPlayer={currentRoom?.QuantityPlayer}");

            var filter = Builders<Room>.Filter.And(
                Builders<Room>.Filter.Eq(r => r.Id, roomId),
                Builders<Room>.Filter.Eq(r => r.Status, RoomStatus.Waiting),
                Builders<Room>.Filter.Lt(r => r.CurrentPlayers, currentRoom?.QuantityPlayer)
            );

            var update = Builders<Room>.Update.Combine(
                Builders<Room>.Update.Push(r => r.Players, new RoomPlayer
                {
                    PlayerId = botId,
                    Name = botName,
                    IsOwner = false,
                    isReady = true
                }),
                Builders<Room>.Update.Inc(r => r.CurrentPlayers, 1)
            );

            var result = await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room> { ReturnDocument = ReturnDocument.After }
            );

            if (result == null)
                throw new InvalidOperationException($"AddBotToRoom failed — roomId={roomId}, status={currentRoom?.Status}, players={currentRoom?.CurrentPlayers}/{currentRoom?.QuantityPlayer}");

            Console.WriteLine($"[AddBot] result null={result == null}");
            return result;
        }

        public async Task<Room> JoinRoom(string roomId, string playerId, string playerName)
        {
            var currentRoom = await GetRoomById(roomId);
            var filter = Builders<Room>.Filter.And(
                Builders<Room>.Filter.Eq(r => r.Id, roomId),
                Builders<Room>.Filter.Eq(r => r.Status, RoomStatus.Waiting),
                Builders<Room>.Filter.Lt(r => r.CurrentPlayers, currentRoom?.QuantityPlayer),
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
                    isReady = false
                }),
                Builders<Room>.Update.Inc(r => r.CurrentPlayers, 1)
            );

            return await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room> { ReturnDocument = ReturnDocument.After }
            );
        }

        public async Task<Room?> LeaveRoom(string roomId, string playerId)
        {
            var room = await GetRoomById(roomId);
            if (room == null) return null;

            var playerToRemove = room.Players?.FirstOrDefault(p => p.PlayerId == playerId);
            if (playerToRemove == null) return room;

            if (room.CurrentPlayers <= 1)
            {
                await DeleteRoom(roomId);
                return null;
            }

            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var update = Builders<Room>.Update
                .PullFilter(r => r.Players, p => p.PlayerId == playerId)
                .Inc(r => r.CurrentPlayers, -1);

            await _roomsCollection.UpdateOneAsync(filter, update);

            if (playerToRemove.IsOwner)
            {
                await TransferOwnership(roomId, playerId);
            }

            var updatedRoom = await GetRoomById(roomId);

            if (updatedRoom == null || updatedRoom.Players?.All(p => p.PlayerId.StartsWith("BOT_")) == true)
            {
                await DeleteRoom(roomId);
                return null;
            }

            return updatedRoom;
        }

        private async Task TransferOwnership(string roomId, string leavingPlayerId)
        {
            var ownerFilter = Builders<Room>.Filter.And(
                Builders<Room>.Filter.Eq(r => r.Id, roomId),
                Builders<Room>.Filter.ElemMatch(r => r.Players, p => p.PlayerId != leavingPlayerId)
            );

            var ownerUpdate = Builders<Room>.Update.Set("Players.$.IsOwner", true);
            await _roomsCollection.UpdateOneAsync(ownerFilter, ownerUpdate);
        }

        public async Task<Room?> StartGame(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var update = Builders<Room>.Update.Set(r => r.Status, RoomStatus.Playing);

            return await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room> { ReturnDocument = ReturnDocument.After }
            );
        }

        public async Task<bool> DeleteRoom(string roomId)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var result = await _roomsCollection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        public async Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status)
        {
            var filter = Builders<Room>.Filter.Eq(r => r.Id, roomId);
            var update = Builders<Room>.Update.Set(r => r.Status, status);

            return await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room> { ReturnDocument = ReturnDocument.After }
            );
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
            return await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room> { ReturnDocument = ReturnDocument.After }
            );
        }

        public async Task<Room> PlayerisReady(string roomId, string playerId, bool isReady)
        {
            Console.WriteLine($"DEBUG - RoomId: {roomId}");
            Console.WriteLine($"DEBUG - PlayerId: {playerId}");

            
            var filter = Builders<Room>.Filter.And(
                Builders<Room>.Filter.Eq(r => r.Id, roomId),
                Builders<Room>.Filter.Eq("players.playerId", playerId)
            );

            var update = Builders<Room>.Update.Set("players.$.isReady", isReady);

            var result = await _roomsCollection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<Room>
                {
                    ReturnDocument = ReturnDocument.After
                }
            );

            Console.WriteLine($"DEBUG - Result is null: {result == null}");

            return result;
        }
    }
}
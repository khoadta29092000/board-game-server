using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.Model.Room;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Service
{
    public class RoomService : IRoomService
    {
        private readonly IRoomRepository _roomRepository;
        private readonly ILogger<RoomService> _logger;

        public RoomService(IRoomRepository roomRepository, ILogger<RoomService> logger)
        {
            _roomRepository = roomRepository;
            _logger = logger;
        }

        public async Task<List<Room>> GetActiveRooms()
        {
            return await _roomRepository.GetActiveRooms();
        }

        public async Task<Room?> GetRoomById(string roomId)
        {
            return await _roomRepository.GetRoomById(roomId);
        }

        public async Task<Room> CreateRoom(Room room)
        {
            _logger.LogInformation("Creating room {RoomId} by player {PlayerId}", room.Id, room.Players?.First()?.PlayerId);
            return await _roomRepository.CreateRoom(room);
        }

        public async Task<Room> JoinRoom(string roomId, string playerId, string playerName)
        {
            // Get room and validate
            var room = await _roomRepository.GetRoomById(roomId);
            RoomValidators.ValidateRoomExists(room, roomId);
            RoomValidators.ValidateRoomStatus(room!, RoomStatus.Waiting);

            // Check if player already in room (for reconnection)
            var existingPlayer = room!.Players?.FirstOrDefault(p => p.PlayerId == playerId);
            if (existingPlayer != null)
            {
                _logger.LogInformation("Player {PlayerId} reconnecting to room {RoomId}", playerId, roomId);
                return room; // Return current room state for reconnection
            }

            // Validate capacity
            RoomValidators.ValidateRoomCapacity(room);

            // Attempt atomic join
            var updatedRoom = await _roomRepository.JoinRoom(roomId, playerId, playerName);
            if (updatedRoom == null)
            {
                throw new RoomFullException(roomId); // Race condition occurred
            }

            _logger.LogInformation("Player {PlayerId} joined room {RoomId}. Current players: {Count}",
                playerId, roomId, updatedRoom.CurrentPlayers);

            return updatedRoom;
        }

        public async Task<Room?> LeaveRoom(string roomId, string playerId)
        {
            var room = await _roomRepository.GetRoomById(roomId);
            RoomValidators.ValidateRoomExists(room, roomId);

            var result = await _roomRepository.LeaveRoom(roomId, playerId);

            _logger.LogInformation("Player {PlayerId} left room {RoomId}", playerId, roomId);
            return result;
        }
        public async Task<Room> PlayerisReady(string roomId, string requestingPlayerId, bool isReady)
        {
            var room = await _roomRepository.GetRoomById(roomId);
            RoomValidators.ValidateRoomExists(room, roomId);
            RoomValidators.ValidateRoomStatus(room!, RoomStatus.Waiting);

            var startedRoom = await _roomRepository.PlayerisReady(roomId, requestingPlayerId, isReady);

            _logger.LogInformation("Player change isReady in room {RoomId} by owner {PlayerId}", roomId, requestingPlayerId);
            return startedRoom!;
        }
        public async Task<Room?> StartGame(string roomId, string requestingPlayerId)
        {
            var room = await _roomRepository.GetRoomById(roomId);
            RoomValidators.ValidateRoomExists(room, roomId);
            RoomValidators.ValidateRoomStatus(room!, RoomStatus.Waiting);
            RoomValidators.ValidateMinimumPlayers(room!);
            RoomValidators.ValidateRoomOwnership(room!, requestingPlayerId);
            RoomValidators.ValidateAllPlayersReady(room!);

            var startedRoom = await _roomRepository.UpdateRoomStatus(roomId, RoomStatus.Playing);

            _logger.LogInformation("Game started in room {RoomId} by owner {PlayerId}", roomId, requestingPlayerId);
            return startedRoom!;
        }


        public async Task<Room?> UpdateRoomSettings(string roomId, string requestingPlayerId, int? maxPlayers, RoomType? roomType)
        {
            var room = await _roomRepository.GetRoomById(roomId);
            RoomValidators.ValidateRoomExists(room, roomId);
            RoomValidators.ValidateRoomOwnership(room!, requestingPlayerId);

            if (maxPlayers.HasValue)
            {
                RoomValidators.ValidateMaxPlayersUpdate(room!, maxPlayers.Value);
            }

            var updatedRoom = await _roomRepository.UpdateRoomSettings(roomId, maxPlayers, roomType);

            _logger.LogInformation("Room settings updated for {RoomId} by owner {PlayerId}", roomId, requestingPlayerId);
            return updatedRoom!;
        }
        public async Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status)

        {
            return await _roomRepository.UpdateRoomStatus(roomId, status);
        }

        public async Task<bool> DeleteRoom(string roomId)
        {
            return await _roomRepository.DeleteRoom(roomId);
        }
    }
}

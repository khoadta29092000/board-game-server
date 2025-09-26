using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
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

        public async Task<List<Room>> GetActiveRoom()
        {
            try
            {
                return await _roomRepository.GetActiveRoom();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active rooms");
                throw new Exception($"Failed to get active rooms: {ex.Message}");
            }
        }

        public async Task<Room?> GetRoomById(string roomId)
        {
            try
            {
                return await _roomRepository.GetRoomById(roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting room {RoomId}", roomId);
                throw new Exception($"Failed to get room: {ex.Message}");
            }
        }

        public async Task<Room> CreateRoom(Room room)
        {
            try
            {
                return await _roomRepository.CreateRoom(room);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating room {RoomId}", room.Id);
                throw new Exception($"Failed to create room: {ex.Message}");
            }
        }

        public async Task<Room?> JoinRoom(string roomId, string playerId, string playerName)
        {
            try
            {
                var room = await _roomRepository.GetRoomById(roomId);
                if (room == null || room.Status != RoomStatus.Waiting ||
                    room.CurrentPlayers >= room.QuantityPlayer)
                {
                    return null; 
                }

                return await _roomRepository.JoinRoom(roomId, playerId, playerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining room {RoomId} for player {PlayerId}", roomId, playerId);
                return null;
            }
        }

        public async Task<Room?> LeaveRoom(string roomId, string playerId)
        {
            try
            {
                return await _roomRepository.LeaveRoom(roomId, playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving room {RoomId} for player {PlayerId}", roomId, playerId);
                throw new Exception($"Failed to leave room: {ex.Message}");
            }
        }

        public async Task<Room?> StartGame(string roomId)
        {
            try
            {
                var room = await _roomRepository.GetRoomById(roomId);
                if (room == null)
                {
                    throw new ArgumentException("Room not found");
                }

                if (room.Status != RoomStatus.Waiting)
                {
                    throw new InvalidOperationException("Room is not in waiting state");
                }

                if (room.CurrentPlayers < 2)
                {
                    throw new InvalidOperationException("Need at least 2 players to start game");
                }

                return await _roomRepository.StartGame(roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting game in room {RoomId}", roomId);
                throw new Exception($"Failed to start game: {ex.Message}");
            }
        }

        public async Task<Room?> UpdateRoomSettings(string roomId, int? maxPlayers, RoomType? roomType)
        {
            try
            {
                var room = await _roomRepository.GetRoomById(roomId);
                if (room == null)
                {
                    throw new ArgumentException("Room not found");
                }

                if (maxPlayers.HasValue && maxPlayers.Value < room.CurrentPlayers)
                {
                    throw new InvalidOperationException("Cannot set max players below current player count");
                }

                return await _roomRepository.UpdateRoomSettings(roomId, maxPlayers, roomType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating room settings for {RoomId}", roomId);
                throw new Exception($"Failed to update room settings: {ex.Message}");
            }
        }

        public async Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status)
        {
            try
            {
                return await _roomRepository.UpdateRoomStatus(roomId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating room status for {RoomId}", roomId);
                throw new Exception($"Failed to update room status: {ex.Message}");
            }
        }

        public async Task<bool> DeleteRoom(string roomId)
        {
            try
            {
                return await _roomRepository.DeleteRoom(roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting room {RoomId}", roomId);
                throw new Exception($"Failed to delete room: {ex.Message}");
            }
        }
    } 
}

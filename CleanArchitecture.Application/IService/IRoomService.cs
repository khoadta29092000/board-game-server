using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IService
{
    public interface IRoomService
    {
        Task<List<Room>> GetActiveRooms();
        Task<Room?> GetRoomById(string roomId);
        Task<Room> CreateRoom(Room room);
        Task<Room?> JoinRoom(string roomId, string playerId, string playerName);
        Task<Room?> LeaveRoom(string roomId, string playerId);
        Task<Room> PlayerisReady(string roomId, string requestingPlayerId, bool isReady);
        Task<Room> StartGame(string roomId, string requestingPlayerId);
        Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status);
        Task<Room> UpdateRoomSettings(string roomId, string requestingPlayerId, int? maxPlayers, RoomType? roomType);
       
        Task<bool> DeleteRoom(string roomId);
    }
}

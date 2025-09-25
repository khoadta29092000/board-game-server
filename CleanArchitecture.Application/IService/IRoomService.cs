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
        Task<List<Room>> GetActiveRoom();
        Task<Room?> GetRoomById(string roomId);
        Task<Room> CreateRoom(Room room);
        Task<Room> JoinRoom(string roomId, string playerId, string playerName);
        Task<Room?> LeaveRoom(string roomId, string playerId);
        Task<Room?> StartGame(string roomId);
        Task<Room?> UpdateRoomSettings(string roomId, int? maxPlayers, RoomType? roomType);
        Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status);
        Task<bool> DeleteRoom(string roomId);
    }
}

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
        Task<Room?> GetRoomById(string roomID);
        Task CreateRoom(Room room);
        Task JoinRoom(string roomId, string playerId, string playerName);
        Task LeaveRoom(string roomId, string playerId);
        Task StartGame(string roomId);
        Task UpdateRoomSettings(string roomId, int? maxPlayers = null, RoomType? roomType = null);
    }
}

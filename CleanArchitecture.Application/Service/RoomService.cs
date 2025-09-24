using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Room;

namespace CleanArchitecture.Application.Service
{
    public class RoomService : IRoomService
    {
        private readonly IRoomRepository roomRepository;
        public RoomService(IRoomRepository roomRepository)
        {
            this.roomRepository = roomRepository;
        }
        Task<List<Room>> IRoomService.GetActiveRoom()
        {
            return roomRepository.GetActiveRoom();
        }
        Task<Room?> IRoomService.GetRoomById(string roomID)
        {
            return roomRepository.GetRoomById(roomID);
        }
        Task IRoomService.CreateRoom(Room room)
        {
            return roomRepository.CreateRoom(room);
        }
        Task IRoomService.JoinRoom(string roomId, string playerId, string playerName)
        {
            return roomRepository.JoinRoom(roomId, playerId, playerName);
        }
        Task IRoomService.LeaveRoom(string roomId, string playerId)
        {
            return roomRepository.LeaveRoom(roomId, playerId);
        }
        Task IRoomService.StartGame(string roomId)
        {
            return roomRepository.StartGame(roomId);
        }
        Task IRoomService.UpdateRoomSettings(string roomId, int? maxPlayers, RoomType? roomType)
        {
            return roomRepository.UpdateRoomSettings(roomId, maxPlayers, roomType);
        }
    }
}

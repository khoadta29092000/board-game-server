using CleanArchitecture.Domain.Model.Room;


namespace CleanArchitecture.Application.IRepository
{
    public interface IRoomRepository
    {
        Task<List<Room>> GetActiveRoom();
        Task<Room?> GetRoomById(string roomId);
        Task<Room> CreateRoom(Room room);
        Task<Room> JoinRoom(string roomId, string playerId, string playerName);
        Task<Room?> LeaveRoom(string roomId, string playerId);
        Task<bool> DeleteRoom(string roomId);
        Task<Room?> StartGame(string roomId);
        Task<Room?> UpdateRoomSettings(string roomId, int? maxPlayers, RoomType? roomType);
        Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status);
    }
}

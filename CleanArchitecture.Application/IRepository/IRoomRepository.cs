using CleanArchitecture.Domain.Model.Room;


namespace CleanArchitecture.Application.IRepository
{
    public interface IRoomRepository
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

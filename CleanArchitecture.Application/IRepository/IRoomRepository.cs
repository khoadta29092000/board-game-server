using CleanArchitecture.Domain.Model.Room;


namespace CleanArchitecture.Application.IRepository
{
    public interface IRoomRepository
    {
        Task<List<Room>> GetActiveRooms();
        Task<Room?> GetRoomById(string roomId);
        Task<Room> CreateRoom(Room room);
        Task<Room?> JoinRoom(string roomId, string playerId, string playerName);
        Task<Room?> LeaveRoom(string roomId, string playerId);
        Task<Room> PlayerisReady(string roomId, string requestingPlayerId, bool isReady);
        Task<Room?> StartGame(string roomId);
        Task<Room?> UpdateRoomStatus(string roomId, RoomStatus status);
        Task<Room?> UpdateRoomSettings(string roomId, int? maxPlayers, RoomType? roomType);
     
        Task<bool> DeleteRoom(string roomId);
    }
}

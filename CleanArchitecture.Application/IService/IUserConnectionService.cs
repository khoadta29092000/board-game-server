using CleanArchitecture.Domain.Model.UserConnection;

namespace CleanArchitecture.Application.IService
{
    public interface IUserConnectionService
    {
        Task AddUserConnection(string playerId, string connectionId, string roomId);
        Task RemoveConnection(string connectionId);
        Task RemoveUserFromRoom(string playerId, string roomId);
        Task<List<string>> GetUserConnections(string playerId);
        Task<UserConnection?> GetUserByConnection(string connectionId);
        Task<bool> IsUserInRoom(string playerId, string roomId);
        Task<List<string>> GetUsersInRoom(string roomId);
        Task RemoveAllConnectionsForUser(string playerId);
    }
}

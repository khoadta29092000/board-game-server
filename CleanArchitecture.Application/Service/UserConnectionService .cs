using CleanArchitecture.Application.IService;
using Microsoft.Extensions.Caching.Memory;
using CleanArchitecture.Domain.Model.UserConnection;

namespace CleanArchitecture.Application.Service
{
    public class UserConnectionService : IUserConnectionService
    {
        private readonly IMemoryCache _cache;
        private readonly string CONNECTION_PREFIX = "conn:";
        private readonly string USER_PREFIX = "user:";

        public UserConnectionService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task AddUserConnection(string playerId, string connectionId, string roomId)
        {
            // Lưu mapping: connectionId -> UserConnection
            var userConnection = new UserConnection
            {
                PlayerId = playerId,
                ConnectionId = connectionId,
                RoomId = roomId,
                ConnectedAt = DateTime.UtcNow
            };

            _cache.Set($"{CONNECTION_PREFIX}{connectionId}", userConnection, TimeSpan.FromHours(24));

            // Lưu mapping: playerId -> List<connectionId>
            var userConnections = _cache.Get<List<string>>($"{USER_PREFIX}{playerId}") ?? new List<string>();
            if (!userConnections.Contains(connectionId))
            {
                userConnections.Add(connectionId);
                _cache.Set($"{USER_PREFIX}{playerId}", userConnections, TimeSpan.FromHours(24));
            }

            await Task.CompletedTask;
        }

        public async Task RemoveConnection(string connectionId)
        {
            var userConnection = _cache.Get<UserConnection>($"{CONNECTION_PREFIX}{connectionId}");
            if (userConnection != null)
            {
                // Remove connection mapping
                _cache.Remove($"{CONNECTION_PREFIX}{connectionId}");

                // Remove from user's connection list
                var userConnections = _cache.Get<List<string>>($"{USER_PREFIX}{userConnection.PlayerId}");
                if (userConnections != null)
                {
                    userConnections.Remove(connectionId);
                    if (userConnections.Any())
                    {
                        _cache.Set($"{USER_PREFIX}{userConnection.PlayerId}", userConnections, TimeSpan.FromHours(24));
                    }
                    else
                    {
                        _cache.Remove($"{USER_PREFIX}{userConnection.PlayerId}");
                    }
                }
            }

            await Task.CompletedTask;
        }

        public async Task RemoveUserFromRoom(string playerId, string roomId)
        {
            var userConnections = await GetUserConnections(playerId);
            foreach (var connectionId in userConnections)
            {
                var connection = _cache.Get<UserConnection>($"{CONNECTION_PREFIX}{connectionId}");
                if (connection?.RoomId == roomId)
                {
                    connection.RoomId = null; // Remove from room but keep connection
                    _cache.Set($"{CONNECTION_PREFIX}{connectionId}", connection, TimeSpan.FromHours(24));
                }
            }
        }

        public async Task<List<string>> GetUserConnections(string playerId)
        {
            var connections = _cache.Get<List<string>>($"{USER_PREFIX}{playerId}") ?? new List<string>();
            return await Task.FromResult(connections);
        }

        public async Task<UserConnection> GetUserByConnection(string connectionId)
        {
            var connection = _cache.Get<UserConnection>($"{CONNECTION_PREFIX}{connectionId}");
            return await Task.FromResult(connection);
        }
    }
}

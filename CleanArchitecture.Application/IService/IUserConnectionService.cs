using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IService
{
    public interface IUserConnectionService
    {
        Task AddUserConnection(string playerId, string connectionId, string roomId);
        Task RemoveConnection(string connectionId);
        Task RemoveUserFromRoom(string playerId, string roomId);
        Task<List<string>> GetUserConnections(string playerId);
    }
}

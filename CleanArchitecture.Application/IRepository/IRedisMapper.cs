using CleanArchitecture.Domain.Model.Splendor.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IRepository
{
    public interface IRedisMapper
    {
        Task SyncGameStateToRedis(GameContext context, string gameId);
        Task<string?> GetGameInfo(string gameId);
        Task<Dictionary<string, string>> GetPlayers(string gameId);
        Task<string?> GetBoard(string gameId);
        Task<string?> GetTurn(string gameId);
        Task DeleteGame(string gameId);
    }
}

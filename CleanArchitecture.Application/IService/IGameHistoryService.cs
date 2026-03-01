using CleanArchitecture.Domain.Model.History;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IService
{
    public interface IGameHistoryService
    {
        Task SaveAsync(GameHistory history);
        Task<GameHistory?> GetByGameIdAsync(string gameId);
        Task<List<GameHistory>> GetByPlayerIdAsync(string playerId, string? gameName = null, int limit = 20);
        Task<List<GameHistory>> GetByGameNameAsync(string gameName, int skip = 0, int limit = 20);
        Task<List<string>> GetGameNamesAsync();
    }
}

using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.History;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.Service
{
    public class GameHistoryService : IGameHistoryService
    {
        private readonly IGameHistoryRepository _historyRepository;

        public GameHistoryService(IGameHistoryRepository historyRepository)
        {
            _historyRepository = historyRepository;
        }

        public Task SaveAsync(GameHistory history) =>
            _historyRepository.SaveAsync(history);

        public Task<GameHistory?> GetByGameIdAsync(string gameId) =>
            _historyRepository.GetByGameIdAsync(gameId);

        public Task<List<GameHistory>> GetByPlayerIdAsync(string playerId, string? gameName = null, int limit = 20) =>
            _historyRepository.GetByPlayerIdAsync(playerId, gameName, limit);

        public Task<List<GameHistory>> GetByGameNameAsync(string gameName, int skip = 0, int limit = 20) =>
            _historyRepository.GetByGameNameAsync(gameName, skip, limit);

        public Task<List<string>> GetGameNamesAsync() =>
            _historyRepository.GetGameNamesAsync();
    }
}

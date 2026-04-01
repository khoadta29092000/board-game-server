using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.Service
{
    public class GameStateService : IGameStateService
    {
        private readonly IGameStateRepository _gameStateRepository;

        public GameStateService(IGameStateRepository gameStateRepository)
        {
            _gameStateRepository = gameStateRepository;
        }

        public async Task<Dictionary<string, List<int>>> GetAvailableGameNamesAsync()
            => await _gameStateRepository.GetAvailableGameNamesAsync();

        public async Task<bool> GameExistsAsync(string gameName)
            => await _gameStateRepository.GameExistsAsync(gameName);
    }
}

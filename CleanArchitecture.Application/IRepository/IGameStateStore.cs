using CleanArchitecture.Domain.Model.Splendor.System;
using CleanArchitecture.Domain.DTO.Splendor;

namespace CleanArchitecture.Application.IRepository
{
    public interface IGameStateStore
    {
        Task SaveGameContext(string roomCode, GameContext context);
        Task<GameContext?> LoadGameContext(string roomCode);
        Task DeleteGameContext(string roomCode);
        Task<List<GroupedGameKeys>> GetGroupedGameKeys(string pattern = "game:*", int limit = 50);
        Task<List<FormattedGameData>> GetFormattedGamesData(string pattern = "game:*", int limit = 50);
        Task<FormattedGameData?> GetGameDataById(string gameId);
        Task<object> GetFormattedValue(string key);
        Task<bool> GameExists(string gameId);
    }
   

  
}

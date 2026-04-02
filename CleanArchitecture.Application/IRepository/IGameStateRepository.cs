using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IRepository
{
    public interface IGameStateRepository
    {
        Task<Dictionary<string, List<int>>> GetAvailableGameNamesAsync();
        Task<bool> GameExistsAsync(string gameName);
    }
}

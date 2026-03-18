using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IService
{
    public interface IGameNotifier
    {
        Task BroadcastGameStateAsync(string roomCode);
        Task NotifyBotThinkingAsync(string roomCode);
        Task NotifyGameOverAsync(string roomCode, string? winnerId);
    }
}

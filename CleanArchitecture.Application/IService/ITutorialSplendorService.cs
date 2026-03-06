using CleanArchitecture.Domain.DTO.Splendor;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.IService
{
    public interface ITutorialSplendorService
    {
        Task<GameContext> StartTutorialAsync(string playerId, string playerName);

        Task<CollectGemResult> CollectGemsAsync(string playerId, Dictionary<GemColor, int> gems);

        Task<bool> DiscardGemsAsync(string playerId, Dictionary<GemColor, int> gems);

        Task<PurchaseCardResult> PurchaseCardAsync(string playerId, Guid cardId);

        Task<SelectNobleResult> SelectNobleAsync(string playerId, Guid nobleId);

        Task<ReserveCardResult> ReserveCardAsync(string playerId, Guid? cardId, int? level = null);

        Task<GameContext?> GetTutorialStateAsync(string playerId);

        Task EndTutorialAsync(string playerId);
    }
}

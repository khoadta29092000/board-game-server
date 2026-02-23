using CleanArchitecture.Domain.Model.Room;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using CleanArchitecture.Domain.Model.Splendor.System;


namespace CleanArchitecture.Application.IService
{
    public interface ISplendorService
    {
        Task<GameContext> StartGameAsync(string roomCode, List<RoomPlayer> playerIds);
        Task<GameContext?> GetGameAsync(string roomCode);
        Task<bool> ForceStartGameAsync(string roomCode);

        Task<object> CollectGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems);
        Task<bool> PurchaseCardAsync(string roomCode, string playerId, Guid cardId);
        Task<bool> SelectNobleAsync(string roomCode, string playerId, Guid nobleId);
        Task<bool> ReserveCardAsync(string roomCode, string playerId, Guid cardId, int? level = null);
        Task<bool> DiscardGemsAsync(string roomCode, string playerId, Dictionary<GemColor, int> gems);
        Task EndTurnAsync(string roomCode, string playerId);
    }
}

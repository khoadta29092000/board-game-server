using CleanArchitecture.Application.IRepository;
using StackExchange.Redis;

namespace CleanArchitecture.Infrastructure.Repository
{
    /// <summary>
    /// Implementation của ITutorialSessionRepository dùng Redis.
    /// Cùng pattern với RedisGameStateStore — inject IConnectionMultiplexer, dùng IDatabase.
    /// </summary>
    public class RedisTutorialSessionRepository : ITutorialSessionRepository
    {
        private readonly IDatabase _db;

        // Set chứa tất cả playerId đang disconnect
        private const string DisconnectedSetKey = "tutorial:disconnected";

        // Key lưu timestamp disconnect theo từng player
        private const string DisconnectTimePrefix = "tutorial:disconnect_time:";

        // Key lưu step:phase theo từng player
        private const string StepPrefix = "tutorial:step:";

        private const string RoomCodePrefix = "tutorial:roomcode:";

        public RedisTutorialSessionRepository(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
        }

        public async Task MarkDisconnectedAsync(string playerId, string roomCode)
        {
            var timeKey = $"{DisconnectTimePrefix}{playerId}";
            var roomKey = $"{RoomCodePrefix}{playerId}";

            await Task.WhenAll(
                _db.SetAddAsync(DisconnectedSetKey, playerId),
                _db.StringSetAsync(timeKey,
                    DateTimeOffset.UtcNow.ToString("O"),
                    expiry: TimeSpan.FromMinutes(15)),
                _db.StringSetAsync(roomKey,
                    roomCode,
                    expiry: TimeSpan.FromMinutes(15))  // cùng TTL
            );
        }
        public async Task SaveRoomCodeAsync(string playerId, string roomCode)
        {
            await _db.StringSetAsync(
                $"{RoomCodePrefix}{playerId}",
                roomCode,
                expiry: TimeSpan.FromHours(2)
            );
        }

        public async Task<string?> GetRoomCodeAsync(string playerId)
        {
            var raw = await _db.StringGetAsync($"{RoomCodePrefix}{playerId}");
            return raw.HasValue ? raw.ToString() : null;
        }

        public async Task ClearDisconnectMarkAsync(string playerId)
        {
            await Task.WhenAll(
                _db.SetRemoveAsync(DisconnectedSetKey, playerId),
                _db.KeyDeleteAsync($"{DisconnectTimePrefix}{playerId}"),
                _db.KeyDeleteAsync($"{RoomCodePrefix}{playerId}")
            );
        }

        public async Task<List<string>> GetDisconnectedPlayerIdsAsync()
        {
            var members = await _db.SetMembersAsync(DisconnectedSetKey);
            return members.Select(m => m.ToString()).ToList();
        }

        public async Task<DateTimeOffset?> GetDisconnectTimeAsync(string playerId)
        {
            var raw = await _db.StringGetAsync($"{DisconnectTimePrefix}{playerId}");
            if (!raw.HasValue) return null;

            if (DateTimeOffset.TryParse(raw.ToString(), out var result))
                return result;

            return null;
        }

        public async Task RemoveDisconnectDataAsync(string playerId)
        {
            await Task.WhenAll(
                _db.SetRemoveAsync(DisconnectedSetKey, playerId),
                _db.KeyDeleteAsync($"{DisconnectTimePrefix}{playerId}"),
                _db.KeyDeleteAsync($"{RoomCodePrefix}{playerId}")
            );
        }

        public async Task SaveStepAsync(string playerId, int stepIndex, string phase)
        {
            await _db.StringSetAsync(
                $"{StepPrefix}{playerId}",
                $"{stepIndex}:{phase}",
                expiry: TimeSpan.FromHours(2)
            );
        }

        public async Task<(int stepIndex, string phase)?> LoadStepAsync(string playerId)
        {
            var raw = await _db.StringGetAsync($"{StepPrefix}{playerId}");
            if (!raw.HasValue) return null;

            var parts = raw.ToString().Split(':');
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[0], out var step)) return null;

            return (step, parts[1]);
        }

        public async Task DeleteStepAsync(string playerId)
        {
            await _db.KeyDeleteAsync($"{StepPrefix}{playerId}");
        }
    }
}
using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Service
{
    /// <summary>
    /// Background service: cứ 60s kiểm tra tutorial session đang disconnect.
    /// Nếu quá 5 phút không reconnect → xóa Redis session.
    ///
    /// Inject trực tiếp vì toàn bộ project dùng Singleton — không cần CreateScope.
    /// </summary>
    public class TutorialCleanupService : BackgroundService
    {
        private readonly ITutorialSessionRepository _sessionRepo;
        private readonly IGameStateStore _stateStore;
        private readonly IRedisMapper _redisMapper;
        private readonly ILogger<TutorialCleanupService> _logger;

        public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

        public TutorialCleanupService(
            ITutorialSessionRepository sessionRepo,
            IGameStateStore stateStore,
            IRedisMapper redisMapper,
            ILogger<TutorialCleanupService> logger)
        {
            _sessionRepo = sessionRepo;
            _stateStore = stateStore;
            _redisMapper = redisMapper;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TutorialCleanupService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(CheckInterval, stoppingToken);

                _logger.LogInformation("🔄 Cleanup tick at {Time}", DateTimeOffset.UtcNow);

                try
                {
                    await CleanupExpiredSessionsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TutorialCleanupService scan error");
                }
            }
        }

        private async Task CleanupExpiredSessionsAsync()
        {
            var disconnectedPlayers = await _sessionRepo.GetDisconnectedPlayerIdsAsync();
            if (disconnectedPlayers.Count == 0) return;

            foreach (var playerId in disconnectedPlayers)
            {
                try
                {
                    var disconnectTime = await _sessionRepo.GetDisconnectTimeAsync(playerId);
                    if (disconnectTime == null)
                    {
                        await _sessionRepo.RemoveDisconnectDataAsync(playerId);
                        continue;
                    }

                    var elapsed = DateTimeOffset.UtcNow - disconnectTime.Value;
                    if (elapsed < GracePeriod) continue;

                    // Lấy roomCode từ Redis thay vì in-memory
                    var roomCode = await _sessionRepo.GetRoomCodeAsync(playerId);
                    if (string.IsNullOrEmpty(roomCode))
                    {
                        _logger.LogWarning("RoomCode not found in Redis for {PlayerId}, skip game delete", playerId);
                        await _sessionRepo.RemoveDisconnectDataAsync(playerId);
                        continue;
                    }

                    await Task.WhenAll(
                        _stateStore.DeleteGameContext(roomCode),
                        _redisMapper.DeleteGame(roomCode),
                        _sessionRepo.RemoveDisconnectDataAsync(playerId),
                        _sessionRepo.DeleteStepAsync(playerId)
                    );

                    _logger.LogInformation(
                        "Tutorial expired for {PlayerId} after {Minutes:F1} min",
                        playerId, elapsed.TotalMinutes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up tutorial for {PlayerId}", playerId);
                }
            }
        }
    }
}
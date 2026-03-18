using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Application.Service;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CleanArchitecture.SignalR.Hubs
{
    public class TutorialGameHub : Hub
    {
        private readonly ITutorialSplendorService _tutorialService;
        private readonly IBotService _botService;
        private readonly IRedisMapper _redisMapper;
        private readonly ITutorialSessionRepository _sessionRepo;
        private readonly ILogger<TutorialGameHub> _logger;

        public TutorialGameHub(
            ITutorialSplendorService tutorialService,
            IServiceProvider serviceProvider,
            IRedisMapper redisMapper,
            ITutorialSessionRepository sessionRepo,
            ILogger<TutorialGameHub> logger)
        {
            _tutorialService = tutorialService;
            _botService = serviceProvider.GetRequiredKeyedService<IBotService>("rule");
            _redisMapper = redisMapper;
            _sessionRepo = sessionRepo;
            _logger = logger;
        }

        // =====================================================================
        // PASS TURN — player gọi khi không có action hợp lệ nào
        // =====================================================================
        public async Task<object> PassTurn(string playerId)
        {
            try
            {
                var success = await _tutorialService.PassTurnAsync(playerId);
                if (!success)
                    return new { success = false, message = "Không thể pass turn lúc này." };

                await BroadcastState(playerId);
                await TriggerBotTurn(playerId);
                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] PassTurn failed — playerId={P}", playerId);
                return new { success = false, message = "Lỗi server khi pass turn." };
            }
        }

        // =====================================================================
        // START TUTORIAL / RECONNECT
        // =====================================================================
        public async Task StartTutorial(string playerId, string playerName)
        {
            try
            {
                var roomCode = TutorialSplendorService.GetRoomCode(playerId);
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
                await _sessionRepo.ClearDisconnectMarkAsync(playerId);

                var existingContext = await _tutorialService.GetTutorialStateAsync(playerId);

                if (existingContext != null)
                {
                    // RECONNECT: game context còn sống → restore step
                    var savedStep = await _sessionRepo.LoadStepAsync(playerId);
                    // Chỉ restore GUIDED phase với stepIndex hợp lệ
                    // FREE_PLAY / TRANSITION / null → reset về 0:GUIDED
                    var restorePhase = "GUIDED";

                        await Clients.Caller.SendAsync("TutorialReady", new
                    {
                        roomCode,
                        stepIndex = savedStep.Value.stepIndex,
                        phase = savedStep.Value.phase,
                        isReconnect = true,
                        message = "Kết nối lại thành công! Tiếp tục từ chỗ bạn dừng."
                    });
                    await BroadcastState(playerId);
                    return;
                }

                // Game context KHÔNG còn (bị cleanup hoặc chưa tạo)
                // → xóa step cũ (nếu có) để tránh restore step lệch với board mới
                await _sessionRepo.DeleteStepAsync(playerId);

                // NEW SESSION
                await _tutorialService.StartTutorialAsync(playerId, playerName);
                await Clients.Caller.SendAsync("TutorialReady", new
                {
                    roomCode,
                    stepIndex = 0,
                    phase = "GUIDED",
                    isReconnect = false,
                    message = "Tutorial bắt đầu! Bạn đi trước."
                });

                await BroadcastState(playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] StartTutorial failed — playerId={PlayerId}", playerId);
                await Clients.Caller.SendAsync("Error", new { message = "Không thể bắt đầu tutorial.", code = "START_TUTORIAL_ERROR" });
            }
        }

        // =====================================================================
        // SAVE STEP — FE gọi mỗi khi stepIndex / phase thay đổi
        // =====================================================================
        public async Task SaveTutorialStep(string playerId, int stepIndex, string phase)
        {
            try { await _sessionRepo.SaveStepAsync(playerId, stepIndex, phase); }
            catch (Exception ex) { _logger.LogError(ex, "[Hub] SaveTutorialStep failed — playerId={P}", playerId); }
        }

        // =====================================================================
        // DELETE STEP — FE gọi khi chuyển sang FREE_PLAY / TRANSITION / DONE
        // Xóa step khỏi Redis để reconnect luôn reset về 0:GUIDED
        // =====================================================================
        public async Task DeleteTutorialStep(string playerId)
        {
            try { await _sessionRepo.DeleteStepAsync(playerId); }
            catch (Exception ex) { _logger.LogError(ex, "[Hub] DeleteTutorialStep failed — playerId={P}", playerId); }
        }

        // =====================================================================
        // COLLECT GEMS
        // =====================================================================
        public async Task<object> CollectGems(string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                var result = await _tutorialService.CollectGemsAsync(playerId, gems);
                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Lấy gem không hợp lệ.", code = "COLLECT_GEM_ERROR" });
                    return new { success = false, message = "Lấy gem không hợp lệ." };
                }
                await BroadcastState(playerId);
                if (result.NeedsDiscard)
                {
                    await Clients.Caller.SendAsync("NeedsDiscard", new { currentGems = result.CurrentGems, excessCount = result.TotalGems - 10 });
                    return new { success = true };
                }
                await TriggerBotTurn(playerId);
                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] CollectGems failed — playerId={P}", playerId);
                return new { success = false, message = "Lỗi server khi lấy gem." };
            }
        }

        // =====================================================================
        // DISCARD GEMS
        // =====================================================================
        public async Task<object> DiscardGems(string playerId, Dictionary<GemColor, int> gems)
        {
            try
            {
                var success = await _tutorialService.DiscardGemsAsync(playerId, gems);

                if (!success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Bỏ gem không hợp lệ.", code = "DISCARD_GEM_ERROR" });
                    return new { success = false, message = "Bỏ gem không hợp lệ." };
                }

                await BroadcastState(playerId);
                await TriggerBotTurn(playerId);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] DiscardGems failed — playerId={P}", playerId);
                return new { success = false, message = "Lỗi server khi bỏ gem." };
            }
        }

        // =====================================================================
        // PURCHASE CARD
        // =====================================================================
        public async Task<object> PurchaseCard(string playerId, Guid cardId)
        {
            try
            {
                var result = await _tutorialService.PurchaseCardAsync(playerId, cardId);

                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Không đủ gem để mua card này.", code = "PURCHASE_CARD_ERROR" });
                    return new { success = false, message = "Không đủ gem để mua card này." };
                }

                if (result.IsGameOver)
                {
                    await BroadcastGameOverState(playerId);
                    await Clients.Caller.SendAsync("GameOver", new { winner = result.Winner });
                    await Clients.Caller.SendAsync("TutorialCompleted", new
                    {
                        winner = result.Winner,
                        message = "🎉 Chúc mừng! Bạn đã thắng ván tutorial đầu tiên!"
                    });
                    await _tutorialService.EndTutorialAsync(playerId);
                    await _sessionRepo.DeleteStepAsync(playerId);
                    return new { success = true };
                }

                await BroadcastState(playerId);
                if (result.NeedsSelectNoble)
                {
                    await Clients.Caller.SendAsync("NeedSelectNoble", new { eligibleNobles = result.EligibleNobles });
                    return new { success = true };
                }

                await TriggerBotTurn(playerId);
                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] PurchaseCard failed — playerId={P}", playerId);
                return new { success = false, message = "Lỗi server khi mua card." };
            }
        }

        // =====================================================================
        // SELECT NOBLE
        // =====================================================================
        public async Task<object> SelectNoble(string playerId, Guid nobleId)
        {
            try
            {
                var result = await _tutorialService.SelectNobleAsync(playerId, nobleId);

                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Noble không hợp lệ.", code = "SELECT_NOBLE_ERROR" });
                    return new { success = false, message = "Noble không hợp lệ." };
                }

                if (result.IsGameOver)
                {
                    await BroadcastGameOverState(playerId);

                    await Clients.Caller.SendAsync("GameOver", new { winner = result.Winner });

                    await Clients.Caller.SendAsync("TutorialCompleted", new
                    {
                        winner = result.Winner,
                        message = "🎉 Chúc mừng! Bạn đã thắng ván tutorial đầu tiên!"
                    });

                    await _tutorialService.EndTutorialAsync(playerId);
                    await _sessionRepo.DeleteStepAsync(playerId);

                    return new { success = true };
                }

                await BroadcastState(playerId);
                await TriggerBotTurn(playerId);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] SelectNoble failed — playerId={P}", playerId);
                return new { success = false, message = "Lỗi server khi chọn noble." };
            }
        }

        // =====================================================================
        // RESERVE CARD
        // =====================================================================
        public async Task<object> ReserveCard(string playerId, Guid? cardId, int? level = null)
        {
            try
            {
                var result = await _tutorialService.ReserveCardAsync(playerId, cardId, level);

                if (!result.Success)
                {
                    await Clients.Caller.SendAsync("Error", new { message = "Không thể reserve card này.", code = "RESERVE_CARD_ERROR" });
                    return new { success = false, message = "Không thể reserve card này." };
                }

                await BroadcastState(playerId);

                if (result.NeedsDiscard)
                {
                    await Clients.Caller.SendAsync("NeedDiscard", new
                    {
                        currentGems = result.CurrentGems,
                        excessCount = result.TotalGems - 10
                    });

                    return new { success = true };
                }

                await TriggerBotTurn(playerId);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] ReserveCard failed — playerId={P}", playerId);
                return new { success = false, message = "Lỗi server khi reserve card." };
            }
        }

        // =====================================================================
        // TRIGGER BOT TURN
        // =====================================================================
        private async Task TriggerBotTurn(string playerId)
        {
            try
            {
                var roomCode = TutorialSplendorService.GetRoomCode(playerId);

                await Clients.Caller.SendAsync("BotThinking", new { message = "Bot đang suy nghĩ..." });

                await _botService.TakeTurnAsync(roomCode, delayMs: 3000);

                var context = await _tutorialService.GetTutorialStateAsync(playerId);
                if (context == null)
                {
                    await Clients.Caller.SendAsync("TutorialFailed", new
                    {
                        message = "Bot đã thắng lần này! Thử lại nhé 💪"
                    });
                    return;
                }

                await BroadcastState(playerId);
                await Clients.Caller.SendAsync("YourTurn", new { message = "Đến lượt bạn!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] TriggerBotTurn failed — playerId={P}", playerId);
            }
        }

        // =====================================================================
        // BROADCAST STATE
        // =====================================================================
        private async Task BroadcastState(string playerId)
        {
            try
            {
                var roomCode = TutorialSplendorService.GetRoomCode(playerId);

                var info = await _redisMapper.GetGameInfo(roomCode);
                var players = await _redisMapper.GetPlayers(roomCode);
                var board = await _redisMapper.GetBoard(roomCode);
                var turn = await _redisMapper.GetTurn(roomCode);
                var cardDecks = await _redisMapper.GetCardDecks(roomCode);

                var response = new
                {
                    info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                    players = players?.ToDictionary(kv => kv.Key, kv => JsonSerializer.Deserialize<object>(kv.Value)),
                    board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                    turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null,
                    cardDecks = cardDecks != null ? JsonSerializer.Deserialize<object>(cardDecks) : null,
                };

                await Clients.Caller.SendAsync("GameStateUpdated", response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hub] BroadcastState failed — playerId={P}", playerId);
            }
        }

        // =====================================================================
        // BROADCAST GAME OVER STATE
        // =====================================================================
        private async Task BroadcastGameOverState(string playerId)
        {
            var roomCode = TutorialSplendorService.GetRoomCode(playerId);
            var info = await _redisMapper.GetGameInfo(roomCode);
            var players = await _redisMapper.GetPlayers(roomCode);

            var response = new
            {
                info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                players = players?.ToDictionary(kv => kv.Key, kv => JsonSerializer.Deserialize<object>(kv.Value)),
                board = (object?)null,
                turn = (object?)null,
                cardDecks = (object?)null,
            };

            await Clients.Caller.SendAsync("GameStateUpdated", response);
        }

        // =====================================================================
        // DISCONNECT: Ghi timestamp, KHÔNG xóa Redis
        // TutorialCleanupService sẽ xóa sau 5 phút nếu không reconnect
        // =====================================================================
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                // playerId truyền qua query string khi connect hub
                // VD: connection.withUrl("/tutorialHub?playerId=xxx")
                var playerId = Context.GetHttpContext()?.Request.Query["playerId"].ToString();

                if (!string.IsNullOrEmpty(playerId))
                {
                    var context = await _tutorialService.GetTutorialStateAsync(playerId);
                    if (context != null)
                    {
                        // Session còn → đánh dấu disconnect, chờ reconnect
                        await _sessionRepo.MarkDisconnectedAsync(playerId);
                    }
                }
            }
            catch
            {
                // Swallow — không để disconnect handler crash app
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
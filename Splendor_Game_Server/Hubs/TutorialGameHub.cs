using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Application.Service;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace CleanArchitecture.SignalR.Hubs
{
    public class TutorialGameHub : Hub
    {
        private readonly ITutorialSplendorService _tutorialService;
        private readonly IBotService _botService;
        private readonly IRedisMapper _redisMapper;
        private readonly ITutorialSessionRepository _sessionRepo;

        public TutorialGameHub(
            ITutorialSplendorService tutorialService,
            IBotService botService,
            IRedisMapper redisMapper,
            ITutorialSessionRepository sessionRepo)
        {
            _tutorialService = tutorialService;
            _botService = botService;
            _redisMapper = redisMapper;
            _sessionRepo = sessionRepo;
        }

        // =====================================================================
        // START TUTORIAL / RECONNECT
        // =====================================================================
        public async Task StartTutorial(string playerId, string playerName)
        {
            var roomCode = TutorialSplendorService.GetRoomCode(playerId);

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

            // Xóa disconnect mark nếu có (player reconnect trong grace period)
            await _sessionRepo.ClearDisconnectMarkAsync(playerId);

            // Kiểm tra session cũ còn trong Redis không
            var existingContext = await _tutorialService.GetTutorialStateAsync(playerId);

            if (existingContext != null)
            {
                // RECONNECT: session còn sống → restore step cũ
                var savedStep = await _sessionRepo.LoadStepAsync(playerId);

                await Clients.Caller.SendAsync("TutorialReconnected", new
                {
                    roomCode,
                    stepIndex = savedStep?.stepIndex ?? 0,
                    phase = savedStep?.phase ?? "GUIDED",
                    message = "Kết nối lại thành công! Tiếp tục từ chỗ bạn dừng."
                });

                await BroadcastState(playerId);
                return;
            }

            // NEW SESSION
            await _tutorialService.StartTutorialAsync(playerId, playerName);
            await _sessionRepo.SaveStepAsync(playerId, 0, "GUIDED");

            await Clients.Caller.SendAsync("TutorialStarted", new
            {
                roomCode,
                stepIndex = 0,
                phase = "GUIDED",
                message = "Tutorial bắt đầu! Bạn đi trước."
            });

            await BroadcastState(playerId);
        }

        // =====================================================================
        // SAVE STEP — FE gọi mỗi khi stepIndex / phase thay đổi
        // =====================================================================
        public async Task SaveTutorialStep(string playerId, int stepIndex, string phase)
        {
            await _sessionRepo.SaveStepAsync(playerId, stepIndex, phase);
        }

        // =====================================================================
        // COLLECT GEMS
        // =====================================================================
        public async Task CollectGems(string playerId, Dictionary<GemColor, int> gems)
        {
            var result = await _tutorialService.CollectGemsAsync(playerId, gems);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("Error", new { message = "Lấy gem không hợp lệ.", code = "COLLECT_GEM_ERROR" });
                return;
            }

            await BroadcastState(playerId);

            if (result.NeedsDiscard)
            {
                await Clients.Caller.SendAsync("NeedDiscard", new
                {
                    currentGems = result.CurrentGems,
                    excessCount = result.TotalGems - 10
                });
                return;
            }

            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // DISCARD GEMS
        // =====================================================================
        public async Task DiscardGems(string playerId, Dictionary<GemColor, int> gems)
        {
            var success = await _tutorialService.DiscardGemsAsync(playerId, gems);

            if (!success)
            {
                await Clients.Caller.SendAsync("Error", new { message = "Bỏ gem không hợp lệ.", code = "DISCARD_GEM_ERROR" });
                return;
            }

            await BroadcastState(playerId);
            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // PURCHASE CARD
        // =====================================================================
        public async Task PurchaseCard(string playerId, Guid cardId)
        {
            var result = await _tutorialService.PurchaseCardAsync(playerId, cardId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("Error", new { message = "Không đủ gem để mua card này.", code = "PURCHASE_CARD_ERROR" });
                return;
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
                return;
            }

            await BroadcastState(playerId);

            if (result.NeedsSelectNoble)
            {
                await Clients.Caller.SendAsync("NeedSelectNoble", new { eligibleNobles = result.EligibleNobles });
                return;
            }

            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // SELECT NOBLE
        // =====================================================================
        public async Task SelectNoble(string playerId, Guid nobleId)
        {
            var result = await _tutorialService.SelectNobleAsync(playerId, nobleId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("Error", new { message = "Noble không hợp lệ.", code = "SELECT_NOBLE_ERROR" });
                return;
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
                return;
            }

            await BroadcastState(playerId);
            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // RESERVE CARD
        // =====================================================================
        public async Task ReserveCard(string playerId, Guid? cardId, int? level = null)
        {
            var result = await _tutorialService.ReserveCardAsync(playerId, cardId, level);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("Error", new { message = "Không thể reserve card này.", code = "RESERVE_CARD_ERROR" });
                return;
            }

            await BroadcastState(playerId);

            if (result.NeedsDiscard)
            {
                await Clients.Caller.SendAsync("NeedDiscard", new
                {
                    currentGems = result.CurrentGems,
                    excessCount = result.TotalGems - 10
                });
                return;
            }

            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // TRIGGER BOT TURN
        // =====================================================================
        private async Task TriggerBotTurn(string playerId)
        {
            var roomCode = TutorialSplendorService.GetRoomCode(playerId);

            await Clients.Caller.SendAsync("BotThinking", new { message = "Bot đang suy nghĩ..." });

            await _botService.TakeTurnAsync(roomCode, delayMs: 1500);

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

        // =====================================================================
        // BROADCAST STATE
        // =====================================================================
        private async Task BroadcastState(string playerId)
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
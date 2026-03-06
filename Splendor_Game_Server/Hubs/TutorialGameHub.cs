using CleanArchitecture.Application.Service;
using CleanArchitecture.Domain.Model.Splendor.Enum;
using Microsoft.AspNetCore.SignalR;

namespace CleanArchitecture.SignalR.Hubs
{
    public class TutorialGameHub : Hub
    {
        private readonly TutorialSplendorService _tutorialService;
        private readonly BotService _botService;

        public TutorialGameHub(TutorialSplendorService tutorialService, BotService botService)
        {
            _tutorialService = tutorialService;
            _botService = botService;
        }

        // =====================================================================
        // START TUTORIAL
        // Client gọi 1 lần khi vào tutorial
        // =====================================================================
        public async Task StartTutorial(string playerId, string playerName)
        {
            var roomCode = TutorialSplendorService.GetRoomCode(playerId);

            var context = await _tutorialService.StartTutorialAsync(playerId, playerName);

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            await Clients.Caller.SendAsync("TutorialStarted", new
            {
                roomCode,
                message = "Tutorial bắt đầu! Bạn đi trước."
            });

            await BroadcastState(playerId);
        }

        // =====================================================================
        // COLLECT GEMS
        // gems: { "Red": 1, "Green": 1, "Blue": 1 }
        // =====================================================================
        public async Task CollectGems(string playerId, Dictionary<GemColor, int> gems)
        {
            var result = await _tutorialService.CollectGemsAsync(playerId, gems);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("ActionFailed", "Lấy gem không hợp lệ.");
                return;
            }

            await BroadcastState(playerId);

            if (result.NeedsDiscard)
            {
                await Clients.Caller.SendAsync("NeedsDiscard", new
                {
                    totalGems = result.TotalGems,
                    currentGems = result.CurrentGems,
                    message = "Bạn đang giữ quá 10 gem, hãy bỏ bớt."
                });
                return; // Chờ player discard, chưa trigger bot
            }

            // Player xong → trigger bot
            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // DISCARD GEMS (sau khi collect > 10)
        // =====================================================================
        public async Task DiscardGems(string playerId, Dictionary<GemColor, int> gems)
        {
            var success = await _tutorialService.DiscardGemsAsync(playerId, gems);

            if (!success)
            {
                await Clients.Caller.SendAsync("ActionFailed", "Bỏ gem không hợp lệ.");
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
                await Clients.Caller.SendAsync("ActionFailed", "Không đủ gem để mua card này.");
                return;
            }

            // Game over: player thắng
            if (result.IsGameOver)
            {
                await BroadcastState(playerId);
                await Clients.Caller.SendAsync("TutorialCompleted", new
                {
                    winner = result.Winner,
                    message = "🎉 Chúc mừng! Bạn đã thắng ván tutorial đầu tiên! Thử thách bạn bè chưa?"
                });
                await _tutorialService.EndTutorialAsync(playerId);
                return;
            }

            await BroadcastState(playerId);

            // Cần chọn noble
            if (result.NeedsSelectNoble)
            {
                await Clients.Caller.SendAsync("SelectNoble", new
                {
                    eligibleNobles = result.EligibleNobles,
                    message = "Có nhiều Noble muốn ghé thăm bạn! Chọn 1 Noble."
                });
                return; // Chờ player chọn noble, chưa trigger bot
            }

            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // SELECT NOBLE (sau PurchaseCard khi có nhiều noble eligible)
        // =====================================================================
        public async Task SelectNoble(string playerId, Guid nobleId)
        {
            var result = await _tutorialService.SelectNobleAsync(playerId, nobleId);

            if (!result.Success)
            {
                await Clients.Caller.SendAsync("ActionFailed", "Noble không hợp lệ.");
                return;
            }

            if (result.IsGameOver)
            {
                await BroadcastState(playerId);
                await Clients.Caller.SendAsync("TutorialCompleted", new
                {
                    winner = result.Winner,
                    message = "🎉 Chúc mừng! Bạn đã thắng ván tutorial đầu tiên! Thử thách bạn bè chưa?"
                });
                await _tutorialService.EndTutorialAsync(playerId);
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
                await Clients.Caller.SendAsync("ActionFailed", "Không thể reserve card này.");
                return;
            }

            await BroadcastState(playerId);

            if (result.NeedsDiscard)
            {
                await Clients.Caller.SendAsync("NeedsDiscard", new
                {
                    totalGems = result.TotalGems,
                    currentGems = result.CurrentGems,
                    message = "Bạn đang giữ quá 10 gem, hãy bỏ bớt."
                });
                return;
            }

            await TriggerBotTurn(playerId);
        }

        // =====================================================================
        // TRIGGER BOT TURN
        // Gọi sau mỗi action của player hoàn tất
        // Bot delay 1500ms → action → broadcast state
        // =====================================================================
        private async Task TriggerBotTurn(string playerId)
        {
            var roomCode = TutorialSplendorService.GetRoomCode(playerId);

            await Clients.Caller.SendAsync("BotThinking", new
            {
                message = "Bot đang suy nghĩ..."
            });

            // Bot tự delay 1500ms bên trong TakeTurnAsync
            await _botService.TakeTurnAsync(roomCode, delayMs: 1500);

            // Check game over sau lượt bot (bot thắng = tutorial failed → cho chơi lại)
            var context = await _tutorialService.GetTutorialStateAsync(playerId);
            if (context == null)
            {
                // Context bị xóa = game ended
                await Clients.Caller.SendAsync("TutorialFailed", new
                {
                    message = "Bot đã thắng lần này! Thử lại nhé 💪"
                });
                return;
            }

            await BroadcastState(playerId);
            await Clients.Caller.SendAsync("YourTurn", new
            {
                message = "Đến lượt bạn!"
            });
        }

        // =====================================================================
        // BROADCAST STATE: Gửi full game state cho player
        // =====================================================================
        private async Task BroadcastState(string playerId)
        {
            var context = await _tutorialService.GetTutorialStateAsync(playerId);
            if (context == null) return;

            await Clients.Caller.SendAsync("GameStateUpdated", context);
        }

        // =====================================================================
        // DISCONNECT: Cleanup khi player thoát
        // =====================================================================
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Không cleanup Redis ngay — player có thể reconnect
            // Nếu muốn cleanup sau X phút có thể dùng background job
            await base.OnDisconnectedAsync(exception);
        }
    }
}

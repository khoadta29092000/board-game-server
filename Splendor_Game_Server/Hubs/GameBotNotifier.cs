using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Splendor_Game_Server.Hubs
{
    public class GameHubNotifier : IGameNotifier
    {
        private readonly IHubContext<GameHub> _hubContext;
        private readonly IRedisMapper _redisMapper;
        private readonly IGameHistoryService _historyService;

        public GameHubNotifier(
            IHubContext<GameHub> hubContext,
            IRedisMapper redisMapper,
            IGameHistoryService historyService)
        {
            _hubContext = hubContext;
            _redisMapper = redisMapper;
            _historyService = historyService;
        }

        public async Task BroadcastGameStateAsync(string roomCode)
        {
            var info = await _redisMapper.GetGameInfo(roomCode);
            var players = await _redisMapper.GetPlayers(roomCode);
            var board = await _redisMapper.GetBoard(roomCode);
            var turn = await _redisMapper.GetTurn(roomCode);
            var cardDecks = await _redisMapper.GetCardDecks(roomCode);

            var response = new
            {
                info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                players = players.ToDictionary(
                    kv => kv.Key,
                    kv => JsonSerializer.Deserialize<object>(kv.Value)
                ),
                board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null,
                cardDecks = cardDecks != null ? JsonSerializer.Deserialize<object>(cardDecks) : null,
            };

            await _hubContext.Clients.Group($"game:{roomCode}")
                .SendAsync("GameStateUpdated", response);
        }

        public async Task NotifyBotThinkingAsync(string roomCode)
        {
            await _hubContext.Clients.Group($"game:{roomCode}")
                .SendAsync("BotThinking", new { message = "Bot is thinking..." });
        }

        public async Task NotifyGameOverAsync(string roomCode, string? winnerId)
        {
            var history = await _historyService.GetByGameIdAsync(roomCode);
            if (history == null) return;

            var completedResponse = new
            {
                info = new
                {
                    gameId = history.GameId,
                    state = history.State,
                    winnerId = history.WinnerId,
                    players = history.PlayerOrder,
                    completedAt = history.CompletedAt
                },
                players = history.Players.ToDictionary(
                    kv => kv.Key,
                    kv => (object)new
                    {
                        playerId = kv.Value.PlayerId,
                        name = kv.Value.Name,
                        points = kv.Value.Score,
                        isWinner = kv.Value.IsWinner,
                    }
                ),
                board = (object?)null,
                turn = (object?)null,
                cardDecks = (object?)null,
            };

            await _hubContext.Clients.Group($"game:{roomCode}")
                .SendAsync("GameStateUpdated", completedResponse);
            await _hubContext.Clients.Group($"game:{roomCode}")
                .SendAsync("GameOver", new { winner = winnerId });
        }
    }
}

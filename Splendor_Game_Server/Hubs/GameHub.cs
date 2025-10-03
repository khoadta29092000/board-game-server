using CleanArchitecture.Application.IService;
using CleanArchitecture.Infrastructure.Redis;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

namespace Splendor_Game_Server.Hubs
{
    [Authorize]
    public class GameHub : Hub
    {
        private readonly ISplendorService _gameService;
        private readonly GameRedisMapper _redisMapper;

        public GameHub(ISplendorService gameService, GameRedisMapper redisMapper)
        {
            _gameService = gameService;
            _redisMapper = redisMapper;
        }
        public async Task JoinGame(string gameId, string playerId)
        {
            var players = await _redisMapper.GetPlayers(gameId);
            if (!players.ContainsKey(playerId))
            {
                throw new HubException("You are not a participant of this game");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, $"game:{gameId}");

            var info = await _redisMapper.GetGameInfo(gameId);
            var board = await _redisMapper.GetBoard(gameId);
            var turn = await _redisMapper.GetTurn(gameId);

            var response = new
            {
                info = info != null ? JsonSerializer.Deserialize<object>(info) : null,
                players = players.ToDictionary(
                    kv => kv.Key,
                    kv => JsonSerializer.Deserialize<object>(kv.Value)
                ),
                board = board != null ? JsonSerializer.Deserialize<object>(board) : null,
                turn = turn != null ? JsonSerializer.Deserialize<object>(turn) : null
            };

            await Clients.Caller.SendAsync("GameStateLoaded", response);
        }
    }
}

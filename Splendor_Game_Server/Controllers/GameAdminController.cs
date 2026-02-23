using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.DTO.Splendor;
using CleanArchitecture.Domain.Model.Splendor.Enum;

using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Splendor_Game_Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameAdminController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IGameStateStore _gameStateStore;
        private readonly ISplendorService _gameService;
        public GameAdminController(IConnectionMultiplexer redis, IGameStateStore gameStateStore, ISplendorService gameService)
        {
            _redis = redis;
            _gameStateStore = gameStateStore;
            _gameService = gameService;
        }

        [HttpGet("ping")]
        public async Task<IActionResult> Ping()
        {
            try
            {
                var db = _redis.GetDatabase();
                var pong = await db.PingAsync();
                return Ok(new { success = true, latencyMs = pong.TotalMilliseconds });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stack = ex.StackTrace });
            }
        }

        [HttpGet("keys")]
        public async Task<IActionResult> GetKeys()
        {
            var games = await _gameStateStore.GetFormattedGamesData("game:*", 50);
            return Ok(new
            {
                total = games.Count,
                games = games
            });
        }
        [HttpPost("game/{roomCode}/force-start")]
        public async Task<IActionResult> ForceStartGame(string roomCode)
        {
            var success = await _gameService.ForceStartGameAsync(roomCode);
            return Ok(new { success });
        }
        [HttpPost("CollectGemsAsync")]
        public async Task<IActionResult> PostCollectGemsAsync(CollectGemsRequest collect)
        {
            try
            {
                var games = await _gameService.CollectGemsAsync(collect.RoomCode, collect.PlayerId, collect.Gems);
                return Ok(new
                {
                    success = games
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stack = ex.StackTrace });
            }
           
        }
        [HttpPost("DiscardGem")]
        public async Task<IActionResult> DiscardGem(CollectGemsRequest collect)
        {
            try
            {
                var games = await _gameService.DiscardGemsAsync(collect.RoomCode, collect.PlayerId, collect.Gems);
                return Ok(new
                {
                    success = games
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stack = ex.StackTrace });
            }

        }
        [HttpPost("PurchaseCardAsync")]
        public async Task<IActionResult> PurchaseCardAsync(CardRequest request)
        {
            try
            {
                var games = await _gameService.PurchaseCardAsync(request.RoomCode, request.PlayerId, request.CardId);
                return Ok(new
                {
                    success = games
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stack = ex.StackTrace });
            }

        }
        [HttpPost("ReserveCardAsync")]
        public async Task<IActionResult> ReserveCardAsync(CardRequest request)
        {
            try
            {
                var games = await _gameService.ReserveCardAsync(request.RoomCode, request.PlayerId, request.CardId);
                return Ok(new
                {
                    success = games
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stack = ex.StackTrace });
            }

        }
    }
}

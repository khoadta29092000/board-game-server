using CleanArchitecture.Application.IRepository;


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

        public GameAdminController(IConnectionMultiplexer redis, IGameStateStore gameStateStore)
        {
            _redis = redis;
            _gameStateStore = gameStateStore;
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
    }
}

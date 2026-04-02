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
        private readonly IGameStateService _gameStateService;
        public GameAdminController(IConnectionMultiplexer redis, IGameStateService gameStateService)
        {
            _redis = redis;
            _gameStateService = gameStateService;
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

        [HttpGet("GetAvailableGameName")]
        public async Task<IActionResult> GetAvailableGameName()
        {
            try
            {
                var result = await  _gameStateService.GetAvailableGameNamesAsync();
               
                return Ok(new { success = true, Message = "Load successful", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, stack = ex.StackTrace });
            }
        }


    }
}

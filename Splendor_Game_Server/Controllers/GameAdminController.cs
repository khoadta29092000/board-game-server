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
        [HttpGet("manual-connect")]
        public async Task<IActionResult> ManualConnect()
        {
            var logs = new List<string>();

            try
            {
                logs.Add("Creating config...");

                var config = new ConfigurationOptions
                {
                    EndPoints = { "redis-19865.crce264.ap-east-1-1.ec2.cloud.redislabs.com:19865" },
                    User = "default",
                    Password = "7vvw7GViaDQbUkRd6TFgWoeAuWx7ERsw",
                    Ssl = false,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls13,
                    AbortOnConnectFail = true,
                    ConnectTimeout = 30000,
                    Protocol = RedisProtocol.Resp3
                };

                config.CertificateValidation += (sender, cert, chain, errors) =>
                {
                    logs.Add($"SSL validation: {errors}");
                    return true;
                };

                logs.Add("Connecting...");
                var redis = await ConnectionMultiplexer.ConnectAsync(config);

                logs.Add($"IsConnected: {redis.IsConnected}");

                if (redis.IsConnected)
                {
                    var db = redis.GetDatabase();
                    var ping = await db.PingAsync();
                    logs.Add($"PING: {ping.TotalMilliseconds}ms");

                    await db.StringSetAsync("test:manual", "Success!");
                    var value = await db.StringGetAsync("test:manual");
                    logs.Add($"GET: {value}");
                }

                redis.Dispose();

                return Ok(new { success = true, logs });
            }
            catch (Exception ex)
            {
                logs.Add($"Error: {ex.Message}");
                logs.Add($"Inner: {ex.InnerException?.Message}");
                return Ok(new { success = false, logs });
            }
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

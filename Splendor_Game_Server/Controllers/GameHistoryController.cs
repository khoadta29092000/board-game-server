using BusinessObject.DTO;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.DTO.Player;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.VerificationCode;
using CleanArchitecture.Infrastructure.Security;
using EASendMail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Splendor_Game_Server.DTO.Player;
using System.IdentityModel.Tokens.Jwt;

namespace Splendor_Game_Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameHistoryController : ControllerBase
    {
        private readonly IGameHistoryService _historyService;

        public GameHistoryController(IGameHistoryService historyService)
        {
            _historyService = historyService;
        }

        /// <summary>
        /// Lấy tất cả history của 1 loại game (Splendor, Chess, ...)
        /// GET /api/GameHistory/game/Splendor?skip=0&limit=20
        /// </summary>
        [HttpGet("game/{gameName}")]
        public async Task<IActionResult> GetByGameName(string gameName, int skip = 0, int limit = 20)
        {
            try
            {
                var histories = await _historyService.GetByGameNameAsync(gameName, skip, limit);
                return Ok(new { StatusCode = 200, Message = "Load successful", data = histories, Count = histories.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy history của 1 game cụ thể theo gameId (dùng khi reconnect)
        /// GET /api/GameHistory/69a4616f9a117ab95dc60cdc
        /// </summary>
        [HttpGet("{gameId}")]
        public async Task<IActionResult> GetByGameId(string gameId)
        {
            try
            {
                var history = await _historyService.GetByGameIdAsync(gameId);
                if (history is null)
                    return NotFound(new { StatusCode = 404, Message = "Game history not found" });

                return Ok(new { StatusCode = 200, Message = "Load successful", data = history });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy lịch sử chơi của 1 player (tất cả game hoặc lọc theo gameName)
        /// GET /api/GameHistory/player/68d43fb7067642c8b125c1c3?gameName=Splendor&limit=20
        /// </summary>
        [HttpGet("player/{playerId}")]
        [Authorize]
        public async Task<IActionResult> GetByPlayerId(string playerId, string? gameName = null, int limit = 20)
        {
            try
            {
                var histories = await _historyService.GetByPlayerIdAsync(playerId, gameName, limit);
                return Ok(new { StatusCode = 200, Message = "Load successful", data = histories, Count = histories.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy lịch sử chơi của chính mình (từ JWT token)
        /// GET /api/GameHistory/my-history?gameName=Splendor&limit=20
        /// </summary>
        [HttpGet("my-history")]
        [Authorize]
        public async Task<IActionResult> GetMyHistory(string? gameName = null, int limit = 20)
        {
            try
            {
                string? playerId = User.FindFirst("Id")?.Value;
                if (string.IsNullOrEmpty(playerId))
                    return StatusCode(401, new { StatusCode = 401, Message = "Unauthorized" });

                var histories = await _historyService.GetByPlayerIdAsync(playerId, gameName, limit);
                return Ok(new { StatusCode = 200, Message = "Load successful", data = histories, Count = histories.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách tất cả loại game đang có history
        /// GET /api/GameHistory/game-names
        /// </summary>
        [HttpGet("game-names")]
        public async Task<IActionResult> GetGameNames()
        {
            try
            {
                var names = await _historyService.GetGameNamesAsync();
                return Ok(new { StatusCode = 200, Message = "Load successful", data = names });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
    }
}

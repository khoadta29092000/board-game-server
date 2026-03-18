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
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerService playerService;
        private readonly SecurityUtility securityUtility;
        private readonly IWebHostEnvironment _env;

        public PlayersController(IPlayerService playersService, SecurityUtility securityUtility, IWebHostEnvironment env)
        {
            this.playerService = playersService;
            this.securityUtility = securityUtility;
            _env = env;

        }

        [HttpGet]
        //[Authorize]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                List<Player> player = await playerService.GetMembers();
                var Count = player.Count();
                return Ok(new { StatusCode = 200, Message = "Load successful", data = player, Count });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }

        }


        [HttpGet("{id:length(24)}")]
        public async Task<ActionResult> Get(string id)
        {
            try
            {
                var player = await playerService.GetMemberById(id);

                if (player is null)
                {
                    return NotFound();
                }
                return StatusCode(200, new { StatusCode = 200, Message = "Load successful", data = player });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPost("Guest_Token")]
        [AllowAnonymous]
        public IActionResult CreateGuestToken([FromBody] GuestTokenRequest request)
        {
            if (string.IsNullOrEmpty(request?.Name))
                return BadRequest(new { StatusCode = 400, Message = "Name is required" });

            var guestId = $"GUEST_{Guid.NewGuid():N}";

            var guestPlayer = new Player
            {
                Id = guestId,
                Name = request.Name,
                Username = $"guest_{guestId[6..14]}",
                IsActive = true,
                IsVerified = true
            };

            var token = securityUtility.GenerateToken(guestPlayer, 3);
            return Ok(new { StatusCode = 200, Message = "Guest token created", data = token });
        }
        [HttpPost("Login")]
        public async Task<IActionResult> GetLogin(LoginPlayer acc)
        {
            try
            {
                Player customer = await playerService.LoginMember(acc.Username, acc.Password);
                return Ok(new { StatusCode = 200, Message = "Login succedfully", data = securityUtility.GenerateToken(customer) });
            }
            catch (NotVerifiedException ex)
            {
                return StatusCode(402, new { status = 402, code = "NotVerified", message = ex.Message });
            }
            catch (NotActiveException ex)
            {
                return StatusCode(403, new { status = 403, code = "NotActive", message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPost("Verification_Player")]
        public async Task<IActionResult> VerificationPlayer(VerifyPlayer newVerify)
        {
            try
            {
                VerificationCode codePlayer = await playerService.GetVerificationCodeByUsername(newVerify.Username);
                if (codePlayer is null)
                { return StatusCode(400, new { StatusCode = 400, Message = "Verification code validation failed." }); }
                await playerService.VerifyAccount(codePlayer, newVerify.Code);
                Player? customer = await playerService.GetMemberByUsername(newVerify.Username);
                return Ok(new { StatusCode = 200, Message = "Verify Player succedfully", data = newVerify.Mode == "login" ? securityUtility.GenerateToken(customer) : "" });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPost("Reset_Password")]
        public async Task<IActionResult> LostPassword(ResetPassword newVerify)
        {
            try
            {
                VerificationCode codePlayer = await playerService.GetVerificationCodeByUsername(newVerify.Username);
                if (codePlayer is null)
                { return StatusCode(400, new { StatusCode = 400, Message = "Verification code validation failed." }); }
                Player? player = await playerService.GetMemberByUsername(newVerify.Username);
                if (player == null)
                {
                    return StatusCode(400, new { StatusCode = 400, Message = "Username is not exist" });
                }

                await playerService.VerifyAccount(codePlayer, newVerify.Code);

                var saltPassword = securityUtility.GenerateSalt();
                var hashPassword = securityUtility.GenerateHashedPassword(newVerify.NewPassword, saltPassword);
                await playerService.ChangePassword(player.Id, hashPassword, saltPassword);

                return Ok(new { StatusCode = 200, Message = "ChangePassword successful" });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPost("Refresh_Verification_Code")]
        public async Task<IActionResult> RefreshVerificationCode(RefreshVerificationCodePlayer newVerify)
        {
            try
            {
                var templatePath = Path.Combine(_env.ContentRootPath, "Templates", "VerificationEmail.html");
                if (!System.IO.File.Exists(templatePath))
                {
                    return StatusCode(500, new { StatusCode = 500, Message = "Email template not found" });
                }
                Player? player = await playerService.GetMemberByUsername(newVerify.Username);
                if (player == null)
                { return StatusCode(400, new { StatusCode = 400, Message = "Username is not exist" }); }
                string Id = ObjectId.GenerateNewId().ToString();
                string verificationCode = securityUtility.GenerateVerificationCode();
                var newCode = new VerificationCode
                {
                    Id = Id,
                    Code = verificationCode,
                    CreatedAt = DateTime.UtcNow,
                    Email = newVerify.Username
                };
                await playerService.RefreshVerificationCode(newCode);


                var body = await System.IO.File.ReadAllTextAsync(templatePath);
                body = body.Replace("{{email}}", player.Username)
                   .Replace("{{code}}", verificationCode);
                string subject = "Verification Code";
                SmtpMail oMail = new SmtpMail("TryIt");
                oMail.From = "kenshiro29092000@gmail.com";
                oMail.To = player.Username;
                oMail.Subject = subject;
                oMail.HtmlBody = body;
                SmtpServer oServer = new SmtpServer("smtp.gmail.com");
                oServer.User = "kenshiro29092000@gmail.com";
                oServer.Password = "dtwmrnsyyhudigka";

                // Set 465 port
                oServer.Port = 587;

                // detect SSL/TLS automatically
                oServer.ConnectType = SmtpConnectType.ConnectSTARTTLS;
                EASendMail.SmtpClient oSmtp = new EASendMail.SmtpClient();
                oSmtp.SendMail(oServer, oMail);

                return Ok(new { StatusCode = 200, Message = "send code succedfully", });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPost("Lost_Password")]
        public async Task<IActionResult> LostPassword(RefreshVerificationCodePlayer newVerify)
        {
            try
            {
                var templatePath = Path.Combine(_env.ContentRootPath, "Templates", "ForgetPassword.html");

                // Thêm log này để debug
                Console.WriteLine($"Looking for template at: {templatePath}");
                Console.WriteLine($"File exists: {System.IO.File.Exists(templatePath)}");

                if (!System.IO.File.Exists(templatePath))
                {
                    return StatusCode(500, new { StatusCode = 500, Message = "Email template not found" });
                }
                Player? player = await playerService.GetMemberByUsername(newVerify.Username);
                if (player == null)
                { return StatusCode(400, new { StatusCode = 400, Message = "Username is not exist" }); }
                string Id = ObjectId.GenerateNewId().ToString();
                string verificationCode = securityUtility.GenerateVerificationCode();
                var newCode = new VerificationCode
                {
                    Id = Id,
                    Code = verificationCode,
                    CreatedAt = DateTime.UtcNow,
                    Email = newVerify.Username,
                    CodeType = CodeType.ForgetPassword
                };
                await playerService.RefreshVerificationCode(newCode);
                var body = await System.IO.File.ReadAllTextAsync(templatePath);
                body = body.Replace("{{email}}", player.Username)
                   .Replace("{{code}}", verificationCode);
                string subject = "Reset Password";
                SmtpMail oMail = new SmtpMail("TryIt");
                oMail.From = "kenshiro29092000@gmail.com";
                oMail.To = player.Username;
                oMail.Subject = subject;
                oMail.HtmlBody = body;
                SmtpServer oServer = new SmtpServer("smtp.gmail.com");
                oServer.User = "kenshiro29092000@gmail.com";
                oServer.Password = "dtwmrnsyyhudigka";

                // Set 465 port
                oServer.Port = 587;

                // detect SSL/TLS automatically
                oServer.ConnectType = SmtpConnectType.ConnectSTARTTLS; ;
                EASendMail.SmtpClient oSmtp = new EASendMail.SmtpClient();
                oSmtp.SendMail(oServer, oMail);
                return Ok(new { StatusCode = 200, Message = "send code succedfully", });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPost("Register")]
        public async Task<IActionResult> Register(PostPlayer player)
        {
            try
            {
                var templatePath = Path.Combine(_env.ContentRootPath, "Templates", "VerificationEmail.html");
                if (!System.IO.File.Exists(templatePath))
                {
                    return StatusCode(500, new { StatusCode = 500, Message = "Email template not found" });
                }
                if (player.ConfirmPassword != player.Password)
                {
                    return StatusCode(400, new { StatusCode = 400, Message = "Confirm Password not correct password" });
                }
                string Id = ObjectId.GenerateNewId().ToString();
                string codeId = ObjectId.GenerateNewId().ToString();
                var saltPassword = securityUtility.GenerateSalt();
                var hashPassword = securityUtility.GenerateHashedPassword(player.Password, saltPassword);
                string verificationCode = securityUtility.GenerateVerificationCode();

                var newPlayer = new Player
                {
                    Id = Id,
                    Name = player.Name,
                    Username = player.Username,
                    HashedPassword = hashPassword,
                    SaltPassword = saltPassword,
                    IsActive = true,
                    IsVerified = false
                };
                await playerService.AddMember(newPlayer);
                var newCode = new VerificationCode
                {
                    Id = codeId,
                    Code = verificationCode,
                    CreatedAt = DateTime.UtcNow,
                    Email = player.Username,
                    CodeType = CodeType.Verify,
                };
                await playerService.AddVerificationCode(newCode);

                string subject = "Verification Code";
                var body = await System.IO.File.ReadAllTextAsync(templatePath);
                body = body.Replace("{{email}}", player.Username)
                   .Replace("{{code}}", verificationCode);
                SmtpMail oMail = new SmtpMail("TryIt");
                oMail.From = "kenshiro29092000@gmail.com";
                oMail.To = player.Username;
                oMail.Subject = subject;
                oMail.HtmlBody = body;
                SmtpServer oServer = new SmtpServer("smtp.gmail.com");
                oServer.User = "kenshiro29092000@gmail.com";
                oServer.Password = "dtwmrnsyyhudigka";

                // Set 465 port
                oServer.Port = 587;

                // detect SSL/TLS automatically
                oServer.ConnectType = SmtpConnectType.ConnectSTARTTLS; ;
                EASendMail.SmtpClient oSmtp = new EASendMail.SmtpClient();
                oSmtp.SendMail(oServer, oMail);


                return Ok(new { StatusCode = 200, Message = "Register successful" });
            }
            catch (Exception ex)
            {
                return StatusCode(409, new { StatusCode = 409, Message = ex.Message });
            }
        }
        [HttpPost("Login_Google")]
        public async Task<ActionResult> GetLoginGoogle(string token)
        {
            try
            {
                string Id = ObjectId.GenerateNewId().ToString();
                var handler = new JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                string email = jsonToken.Claims.First(claim => claim.Type == "email").Value;
                string avatar = jsonToken.Claims.First(claim => claim.Type == "picture").Value;
                string name = jsonToken.Claims.First(claim => claim.Type == "name").Value;
                var players = await playerService.GetMembers();
                var isExists = players.SingleOrDefault(x => x.Username == email);
                if (isExists == null)
                {
                    var newPlayer = new Player
                    {
                        Username = email,
                        Id = Id,
                        IsActive = true,
                        IsVerified = true,
                        Name = name,
                    };
                    await playerService.AddMember(newPlayer);
                    var member = players.SingleOrDefault(x => x.Username == newPlayer.Username);
                    return Ok(new { StatusCode = 201, Message = "Login SuccessFully", data = securityUtility.GenerateToken(newPlayer) });
                }
                else
                {
                    return Ok(new { StatusCode = 200, Message = "Login SuccessFully", data = securityUtility.GenerateToken(isExists) });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(409, new { StatusCode = 409, Message = ex.Message });
            }
        }
        [HttpPost]
        public async Task<IActionResult> Post(PostPlayer newplayer)
        {
            try
            {
                string Id = ObjectId.GenerateNewId().ToString();
                var saltPassword = securityUtility.GenerateSalt();
                var hashPassword = securityUtility.GenerateHashedPassword(newplayer.Password, saltPassword);
                Player player = new Player
                {


                    Id = Id,
                    IsActive = true,
                    Name = newplayer.Name,
                    Username = newplayer.Username,
                    HashedPassword = hashPassword,
                    SaltPassword = saltPassword,
                    IsVerified = true
                };
                await playerService.AddMember(player);

                return Ok(new { StatusCode = 200, Message = "Create successful", data = player });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }

        [HttpPut("{id:length(24)}")]
        public async Task<IActionResult> Update(string id, Player updatedplayer)
        {
            try
            {
                var player = await playerService.GetMemberById(id);

                if (player is null)
                {
                    return NotFound();
                }

                await playerService.UpdateMember(updatedplayer);

                return Ok(new { StatusCode = 200, Message = "Update successful" });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }

        [HttpDelete("{id:length(24)}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var player = await playerService.GetMemberById(id);

                if (player is null)
                {
                    return NotFound();
                }

                await playerService.DeleteMember(id);

                return Ok(new { StatusCode = 200, Message = "Update successful" });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpPut("Change_Password")]
        [Authorize]
        public async Task<IActionResult> ChangPassword(ChangePasswordPlayer player)
        {
            try
            {
                string? palyerId = User.FindFirst("Id")?.Value;
                Player? oldPlayer = await playerService.GetMemberById(palyerId);
                var saltPassword = securityUtility.GenerateSalt();
                var hashPassword = securityUtility.GenerateHashedPassword(player.OldPassword, saltPassword);
                if (oldPlayer.HashedPassword == null)
                {
                    await playerService.ChangePassword(palyerId, hashPassword, saltPassword);
                    return Ok(new { StatusCode = 200, Message = "ChangePassword successful" });
                }
                if (securityUtility.GenerateHashedPassword(player.OldPassword, oldPlayer.SaltPassword) != oldPlayer.HashedPassword)
                {
                    return Ok(new { StatusCode = 400, Message = "Old Password not correct" });
                }
                else
                {
                    await playerService.ChangePassword(palyerId, hashPassword, saltPassword);
                    return Ok(new { StatusCode = 200, Message = "ChangePassword successful" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
        [HttpGet("Get_Profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                string? playerId = User.FindFirst("Id")?.Value;

                if (playerId?.StartsWith("GUEST_") == true)
                {
                    return Ok(new
                    {
                        StatusCode = 200,
                        Message = "load successful",
                        data = new
                        {
                            Id = playerId,
                            Name = User.FindFirst("Name")?.Value ?? "Guest",
                            Username = User.FindFirst("Email")?.Value ?? ""
                        }
                    });
                }

                Player? oldPlayer = await playerService.GetMemberById(playerId);

                return Ok(new
                {
                    StatusCode = 200,
                    Message = "load successful",
                    data = new
                    {
                        Id = oldPlayer?.Id ?? "",
                        Name = oldPlayer?.Name ?? "",
                        Username = oldPlayer?.Username ?? ""
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(400, new { StatusCode = 400, Message = ex.Message });
            }
        }
    }
}

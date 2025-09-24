using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using CleanArchitecture.Domain.Model.Player;
using System.Security.Cryptography;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace CleanArchitecture.Infrastructure.Security
{
    public class SecurityUtility
    {
        private readonly IConfiguration configuration;
        public SecurityUtility(IConfiguration configuration)
        {
            this.configuration = configuration;
        }
        public string GenerateSalt()
        {
            byte[] saltBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }
            return Convert.ToBase64String(saltBytes);
        }
        public string GenerateHashedPassword(string password, string salt)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] saltBytes = Convert.FromBase64String(salt);

            byte[] hashedPasswordBytes;
            using (var sha256 = SHA256.Create())
            {
                byte[] passwordWithSaltBytes = new byte[passwordBytes.Length + saltBytes.Length];
                Buffer.BlockCopy(passwordBytes, 0, passwordWithSaltBytes, 0, passwordBytes.Length);
                Buffer.BlockCopy(saltBytes, 0, passwordWithSaltBytes, passwordBytes.Length, saltBytes.Length);

                hashedPasswordBytes = sha256.ComputeHash(passwordWithSaltBytes);
            }

            return Convert.ToBase64String(hashedPasswordBytes);
        }
        public string GenerateToken(Player acc)
        {
            var secretKey = configuration.GetSection("AppSettings").GetSection("SecretKey").Value;

            var jwtTokenHandler = new JwtSecurityTokenHandler();

            var secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            var tokenDescription = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]{
                    new Claim("Email", acc.Username),
                    new Claim("Id", acc.Id.ToString()),
                    new Claim("Name", acc.Name),
                    new Claim("TokenId", Guid.NewGuid().ToString()),
                }),
                Expires = DateTime.UtcNow.AddDays(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secretKeyBytes), SecurityAlgorithms.HmacSha512Signature)
            };
            var token = jwtTokenHandler.CreateToken(tokenDescription);

            return jwtTokenHandler.WriteToken(token);
        }
        public string GenerateVerificationCode()
        {
           
            Random random = new Random();
            int code = random.Next(100000, 999999);
            return code.ToString();
        }
    }
}

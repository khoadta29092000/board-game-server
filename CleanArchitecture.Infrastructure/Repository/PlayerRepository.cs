using CleanArchitecture.Application.Repository;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Infrastructure.Security;
using CleanArchitecture.Domain.Model.VerificationCode;
using System.Net.Http.Headers;
using System.Text.Json;
using CleanArchitecture.Domain.Exceptions;

namespace CleanArchitecture.Infrastructure.Repository
{

    public class PlayerRepository : IPlayerRepository
    {
        private readonly IMongoCollection<Player> _playersCollection;
        private readonly IMongoCollection<VerificationCode> _verificationCollection;
        private readonly SecurityUtility securityUtility;

        public PlayerRepository(
           IOptions<DatabaseSettings> playerStoreDatabaseSettings, SecurityUtility securityUtility)
        {
            securityUtility = securityUtility ?? throw new ArgumentNullException(nameof(securityUtility));
            if (playerStoreDatabaseSettings == null || playerStoreDatabaseSettings.Value == null)
            {
                throw new ArgumentNullException(nameof(playerStoreDatabaseSettings), "Database settings are null.");
            }


            var mongoClient = new MongoClient(
                playerStoreDatabaseSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                playerStoreDatabaseSettings.Value.DatabaseName);

            _verificationCollection = mongoDatabase.GetCollection<VerificationCode>(
                playerStoreDatabaseSettings.Value.VerificationCodesCollectionName);
            CreateTTLIndex().Wait();

            _playersCollection = mongoDatabase.GetCollection<Player>(
                playerStoreDatabaseSettings.Value.PlayersCollectionName);

            this.securityUtility = securityUtility;
        }
        private async Task CreateTTLIndex()
        {
            try
            {
                // Thử xóa index cũ nếu tồn tại
                var existingIndex = await _verificationCollection.Indexes.ListAsync();
                var indexes = await existingIndex.ToListAsync();
                var oldTtlIndex = indexes.FirstOrDefault(i => i["name"] == "createdAt_1");
                if (oldTtlIndex != null)
                {
                    await _verificationCollection.Indexes.DropOneAsync(oldTtlIndex["name"].AsString);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error while dropping existing TTL index", ex);
            }

            // Tạo TTL index mới
            var createdAtField = new StringFieldDefinition<VerificationCode>("CreatedAt");
            var ttlIndexDefinition = new IndexKeysDefinitionBuilder<VerificationCode>().Ascending(createdAtField);
            var ttlIndexOptions = new CreateIndexOptions { ExpireAfter = TimeSpan.FromMinutes(30) };
            var ttlIndexModel = new CreateIndexModel<VerificationCode>(ttlIndexDefinition, ttlIndexOptions);

            try
            {
                await _verificationCollection.Indexes.CreateOneAsync(ttlIndexModel);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while creating new TTL index", ex);
            }
        }
        public async Task<GoogleUserInfo?> GetGoogleUserInfoAsync(string accessToken)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GoogleUserInfo>(content);
        }

        public async Task<List<Player>> GetMembers() =>
          await _playersCollection.Find(_ => true).ToListAsync();

        public async Task<Player?> GetMemberById(string id) =>
            await _playersCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<Player?> GetMemberByUsername(string username) =>
           await _playersCollection.Find(x => x.Username == username).FirstOrDefaultAsync();
        public async Task<Player?> LoginMember(string email, string password)
        {
            var member = await _playersCollection
                .Find(x => x.Username == email)
                .FirstOrDefaultAsync();

            if (member is null)
                throw new Exception("Invalid username or password");

            var hashed = securityUtility.GenerateHashedPassword(password, member.SaltPassword);
            if (hashed != member.HashedPassword)
                throw new Exception("Invalid username or password");

            if (!member.IsVerified)
                throw new NotVerifiedException("Account is not verified");

            if (!member.IsActive)
                throw new NotActiveException("Account is not active");

            return member;
        }

        public async Task DeleteMember(string id) =>
           await _playersCollection.DeleteOneAsync(x => x.Id == id);
        public async Task UpdateMember(Player updatedplayer)
        {
            try
            {
                var existingPlayer = await _playersCollection.Find(x => x.Username == updatedplayer.Username && x.Id != updatedplayer.Id).FirstOrDefaultAsync();
                if (existingPlayer != null)
                {
                    throw new Exception("Username is Exits");
                }

                await _playersCollection.ReplaceOneAsync(x => x.Id == updatedplayer.Id, updatedplayer);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
        public async Task AddMember(Player newplayer)
        {
            try
            {
                var existingPlayer = await _playersCollection.Find(x => x.Username == newplayer.Username).FirstOrDefaultAsync();
                if (existingPlayer != null)
                {
                    throw new Exception("Username is Exits");

                }
                await _playersCollection.InsertOneAsync(newplayer);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
        public async Task<VerificationCode> GetVerificationCodeByUsername(string username)
        {
            try
            {
                return await _verificationCollection.Find(x => x.Email == username).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }



        public async Task AddVerificationCode(VerificationCode newCode)
        {
            try
            {
                var existingPlayer = await _verificationCollection.Find(x => x.Code == newCode.Code).FirstOrDefaultAsync();
                if (existingPlayer != null)
                {
                    throw new Exception("newCode is Exits");
                }
                await _verificationCollection.InsertOneAsync(newCode);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
        public async Task RefreshVerificationCode(VerificationCode newVerify)
        {
            try
            {
                var existingPlayer = await _playersCollection.Find(x => x.Username == newVerify.Email).FirstOrDefaultAsync();
                var existingCode = await _verificationCollection.Find(x => x.Email == newVerify.Email).FirstOrDefaultAsync();
                if (existingPlayer == null)
                {
                    throw new Exception("Username is not exit");
                }
                if (existingCode == null)
                {
                    await _verificationCollection.InsertOneAsync(newVerify);
                }
                else
                {

                    var updateDefinition = Builders<VerificationCode>.Update
                        .Set(x => x.Code, newVerify.Code)
                        .Set(x => x.CreatedAt, DateTime.UtcNow);

                    await _verificationCollection.UpdateOneAsync(
                        Builders<VerificationCode>.Filter.Eq(x => x.Email, newVerify.Email),
                        updateDefinition);

                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
        public async Task VerifyAccount(VerificationCode newVerify, string code)
        {
            var existingPlayer = await _playersCollection
                .Find(x => x.Username == newVerify.Email)
                .FirstOrDefaultAsync();

            if (existingPlayer is null)
                throw new Exception("Username does not exist");

            var existingCode = await _verificationCollection
                .Find(x => x.Email == newVerify.Email && x.CodeType == newVerify.CodeType)
                .FirstOrDefaultAsync();

            if (existingCode is null)
                throw new Exception("Verification code not found");

            if (existingCode.Code != code)
                throw new Exception("Incorrect code");

            if ((DateTime.UtcNow - existingCode.CreatedAt).TotalMinutes > 10)
                throw new Exception("Expired code");

            var updateDefinition = Builders<Player>.Update
                .Set(x => x.IsVerified, true);

            await _playersCollection.UpdateOneAsync(
                Builders<Player>.Filter.Eq(x => x.Id, existingPlayer.Id),
                updateDefinition);

            await _verificationCollection.DeleteOneAsync(x => x.Id == existingCode.Id);
        }

        public async Task ChangePassword(string id, string hashedPassword, string saltPassword)
        {
            try
            {
                var existingPlayer = await _playersCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
                if (existingPlayer != null)
                {
                    existingPlayer.HashedPassword = hashedPassword;
                    existingPlayer.SaltPassword = saltPassword;


                    await _playersCollection.ReplaceOneAsync(x => x.Id == id, existingPlayer);
                }
                else
                {

                    throw new Exception("Player not found");
                }
            }
            catch (Exception ex)
            {

                throw new Exception(ex.Message);
            }
        }
    }
}


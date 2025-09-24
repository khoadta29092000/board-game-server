using CleanArchitecture.Application.IService;
using CleanArchitecture.Application.Repository;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.VerificationCode;

namespace CleanArchitecture.Application.Service
{
    public class PlayerService : IPlayerService
    {
        private readonly IPlayerRepository playerRepository;
        public PlayerService(IPlayerRepository playerRepository)
        {
            this.playerRepository = playerRepository;
        }
        Task<List<Player>> IPlayerService.GetMembers()
        {
            return playerRepository.GetMembers();
        }
        Task<Player?> IPlayerService.GetMemberById(string m)
        {
            return playerRepository.GetMemberById(m);
        }
        Task<Player?> IPlayerService.GetMemberByUsername(string m)
        {
            return playerRepository.GetMemberByUsername(m);
        }

        Task<Player> IPlayerService.LoginMember(string email, string password)
        {
            return playerRepository.LoginMember(email, password);
        }
        Task<GoogleUserInfo?> IPlayerService.GetGoogleUserInfoAsync(string accessToken)
        {
            return playerRepository.GetGoogleUserInfoAsync(accessToken);
        }
        Task IPlayerService.DeleteMember(string m)
        {
            return playerRepository.DeleteMember(m);
        }

        Task IPlayerService.UpdateMember(Player m)
        {
            return playerRepository.UpdateMember(m);
        }

        Task IPlayerService.AddMember(Player m)
        {
            return playerRepository.AddMember(m);
        }
        Task IPlayerService.ChangePassword(string id, string hashedPassword, string saltPassword)
        {
            return playerRepository.ChangePassword(id,hashedPassword,saltPassword);
        }
        Task IPlayerService.AddVerificationCode(VerificationCode newCode)
        {
            return playerRepository.AddVerificationCode(newCode);
        }
        Task<VerificationCode> IPlayerService.GetVerificationCodeByUsername(string username)
        {
            return playerRepository.GetVerificationCodeByUsername(username);
        }
        Task IPlayerService.RefreshVerificationCode(VerificationCode newCode)
        {
            return playerRepository.RefreshVerificationCode(newCode);
        }
        Task IPlayerService.VerifyAccount(VerificationCode newCode, string number)
        {
            return playerRepository.VerifyAccount(newCode, number);
        }
    }
}

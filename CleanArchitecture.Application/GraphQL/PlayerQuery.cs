using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.GraphQL
{
    public class PlayerQuery
    {
        private readonly IPlayerService _playerService;
        public PlayerQuery(IPlayerService playerService)
        {
            _playerService = playerService;
        }
        public Task<List<Player>> GetPlayers() => _playerService.GetMembers();
    }
}

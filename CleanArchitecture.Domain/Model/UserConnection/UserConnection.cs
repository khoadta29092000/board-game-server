using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.UserConnection
{
    public class UserConnection
    {
        public string PlayerId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string RoomId { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
    }
}

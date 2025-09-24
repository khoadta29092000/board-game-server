using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Player
{
    public class ResetPassword
    {
        public string Username { get; set; }
        public string Code { get; set; }
        public string NewPassword { get; set; }
    }
}

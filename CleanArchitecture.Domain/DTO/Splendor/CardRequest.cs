using CleanArchitecture.Domain.Model.Splendor.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Splendor
{
    public class CardRequest
    {
        public string RoomCode { get; set; }
        public string PlayerId { get; set; }
        public Guid CardId { get; set; }
    }
}

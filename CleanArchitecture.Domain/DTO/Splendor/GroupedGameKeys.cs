using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Splendor
{
    public class GroupedGameKeys
    {
        public string GameId { get; set; } = string.Empty;
        public List<string> Keys { get; set; } = new();
        public List<string> FullKeys { get; set; } = new();
    }
}

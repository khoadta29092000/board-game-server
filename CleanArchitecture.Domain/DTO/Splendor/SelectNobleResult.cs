using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Splendor
{
    public class SelectNobleResult
    {
        public bool Success { get; set; }
        public bool IsGameOver { get; set; }
        public bool JustTriggeredLastRound { get; set; }
        public string? Winner { get; set; }
    }
}

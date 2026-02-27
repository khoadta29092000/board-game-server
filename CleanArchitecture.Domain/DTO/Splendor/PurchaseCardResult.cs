using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Splendor
{
    public class PurchaseCardResult
    {
        public bool Success { get; set; }
        public bool NeedsSelectNoble { get; set; }
        public List<Guid> EligibleNobles { get; set; } = new();
        public bool IsGameOver { get; set; }
        public bool JustTriggeredLastRound { get; set; }
        public string? Winner { get; set; }
    }
}

using CleanArchitecture.Domain.Model.Splendor.Enum;
using Microsoft.OpenApi.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Splendor
{
    public class CollectGemResult
    {
        public bool Success { get; set; }
        public bool NeedsDiscard { get; set; }
        public int TotalGems { get; set; }
        public Dictionary<GemColor, int> CurrentGems { get; set; } = new();
    }
}

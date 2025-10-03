

namespace CleanArchitecture.Domain.DTO.Splendor
{
    public class FormattedGameData
    {
        public string GameId { get; set; } = string.Empty;
        public List<string> Keys { get; set; } = new();
        public Dictionary<string, object> Data { get; set; } = new();
    }
}

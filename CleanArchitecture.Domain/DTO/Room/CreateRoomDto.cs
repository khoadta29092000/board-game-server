using CleanArchitecture.Domain.Model.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.DTO.Room
{
    public class CreateRoomDto
    {
        [JsonPropertyName("gameName")]
        public string GameName { get; set; } = string.Empty;

        [JsonPropertyName("quantityPlayer")]
        public int QuantityPlayer { get; set; } = 4;

        [JsonPropertyName("roomType")]
        public RoomType RoomType { get; set; } = RoomType.Public;
        [JsonPropertyName("password")]
        public string? Password { get; set; } = null;
    }
}

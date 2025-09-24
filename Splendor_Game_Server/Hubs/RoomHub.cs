using Amazon.Runtime.Internal;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Room;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CleanArchitecture.Presentation.Hubs
{
    [Authorize]
    public class RoomHub : Hub
    {
        private readonly IRoomService _roomService;
        private readonly IUserConnectionService _userConnectionService;
        public RoomHub(IRoomService roomService, IUserConnectionService userConnectionService)
        {
            _roomService = roomService;
            _userConnectionService = userConnectionService;
        }

        private (string? playerId, string? playerName) GetPlayerInfo()
        {
            var playerId = Context.User.FindFirst("Id")?.Value;
            var playerName = Context.User.FindFirst("Name")?.Value;

            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(playerName))
            {
                return (null, null);
            }
            return (playerId, playerName);
        }

        // 1. Join Room List Group để nhận updates về danh sách phòng
        public async Task JoinRoomListGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "RoomList");
        }

        public async Task LeaveRoomListGroup()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "RoomList");
        }

        // 2. Get Active Rooms - thay thế API
        public async Task GetActiveRooms()
        {
            try
            {
                var rooms = await _roomService.GetActiveRoom();
                await Clients.Caller.SendAsync("ActiveRoomsLoaded", rooms);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"can't loading rooms: {ex.Message}");
               
            }
        }
        //3. Tạo Phòng mới
        public async Task CreateRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                if (playerId == null)
                {
                    await Clients.Caller.SendAsync("Error", "Unauthorized: Invalid token");
                    return;
                }
                var room = new Room
                {
                    CurrentPlayers = 1,
                    QuantityPlayer = 4,
                    Id = "room_" + playerName,
                    Players = new List<RoomPlayer>
                    {
                        new RoomPlayer
                        {
                          IsOwner = true,
                          Name = playerName!,
                          PlayerId = playerId!
                        }
                    },
                    RoomType = RoomType.Public,
                    Status = RoomStatus.Waiting
                };
                await _roomService.CreateRoom(room);
                // Track connection mapping
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);

                // Add creator to room group
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");

                // Notify caller về phòng vừa tạo
                await Clients.Caller.SendAsync("RoomCreated", new { room, success = true });

                // Notify cho room list về update (phòng mới được tạo)
                await Clients.Group("RoomList")
                    .SendAsync("RoomUpdated", room);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"can't create room: {ex.Message}");
            }
        }
        // 4. Join Room
        public async Task JoinRoom(string roomId)
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                if (playerId == null)
                {
                    await Clients.Caller.SendAsync("Error", "Unauthorized: Invalid token");
                    return;
                }
                var room = await _roomService.GetRoomById(roomId);
                if (room == null)
                {
                    await Clients.Caller.SendAsync("Error", "Room not found");
                    return;
                }
                if (room.Status != RoomStatus.Waiting)
                {
                    await Clients.Caller.SendAsync("Error", "Game has already started in this room");
                    return;
                }
                if (room.CurrentPlayers >= room.QuantityPlayer)
                {
                    await Clients.Caller.SendAsync("Error", "Room is full");
                    return;
                }
                if (room.Players.Any(p => p.PlayerId == playerId))
                {
                    await Clients.Caller.SendAsync("Error", "You are already in this room");
                    return;
                }
                await _roomService.JoinRoom(roomId, playerId, playerName);

                // Track connection mapping
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);

                // Add to room group
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");

                var updatedRoom = await _roomService.GetRoomById(roomId);

                // Notify cho người vừa join
                await Clients.Caller.SendAsync("JoinedRoom", new { room = updatedRoom, success = true });

                // Notify cho tất cả người trong room (trừ người vừa join)
                await Clients.GroupExcept($"Room_{roomId}", Context.ConnectionId)
                 .SendAsync("PlayerJoined", new { playerId, playerName, room = updatedRoom });

                // Notify cho room list về update
                await Clients.Group("RoomList")
                    .SendAsync("RoomUpdated", updatedRoom);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"can't join room: {ex.Message}");
            }
        }
    }
}

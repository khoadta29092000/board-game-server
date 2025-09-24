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
        private readonly ILogger<RoomHub> _logger;

        public RoomHub(
            IRoomService roomService,
            IUserConnectionService userConnectionService,
            ILogger<RoomHub> logger)
        {
            _roomService = roomService;
            _userConnectionService = userConnectionService;
            _logger = logger;
        }

        private (string? playerId, string? playerName) GetPlayerInfo()
        {
            var playerId = Context.User.FindFirst("Id")?.Value;
            var playerName = Context.User.FindFirst("Name")?.Value;

            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(playerName))
            {
                _logger.LogWarning("⚠️ Invalid player info. ConnectionId: {ConnectionId}", Context.ConnectionId);
                return (null, null);
            }

            return (playerId, playerName);
        }

        public override async Task OnConnectedAsync()
        {
            var user = Context.User?.Identity?.Name ?? "Anonymous";
            _logger.LogInformation("🔌 Client connected: {ConnectionId}, User: {User}", Context.ConnectionId, user);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogWarning("❌ Client disconnected: {ConnectionId}, Reason: {Reason}",
                Context.ConnectionId, exception?.Message);

            await base.OnDisconnectedAsync(exception);
        }

        // 1. Join Room List Group
        public async Task JoinRoomListGroup()
        {
            _logger.LogInformation("➡️ {ConnectionId} joined RoomList group", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, "RoomList");
        }

        public async Task LeaveRoomListGroup()
        {
            _logger.LogInformation("↩️ {ConnectionId} left RoomList group", Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "RoomList");
        }

        // 2. Get Active Rooms
        public async Task GetActiveRooms()
        {
            try
            {
                _logger.LogInformation("📥 GetActiveRooms called by {ConnectionId}", Context.ConnectionId);

                var rooms = await _roomService.GetActiveRoom();

                _logger.LogInformation("📦 Found {RoomCount} active rooms", rooms.Count());

                await Clients.Caller.SendAsync("ActiveRoomsLoaded", rooms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get active rooms. ConnectionId: {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", $"can't loading rooms: {ex.Message}");
            }
        }

        // 3. Create Room
        public async Task CreateRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                _logger.LogInformation("➡️ CreateRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized CreateRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
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
                        new RoomPlayer { IsOwner = true, Name = playerName!, PlayerId = playerId! }
                    },
                    RoomType = RoomType.Public,
                    Status = RoomStatus.Waiting
                };

                await _roomService.CreateRoom(room);
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");

                _logger.LogInformation("✅ Room created: {RoomId} by {PlayerName}", room.Id, playerName);

                await Clients.Caller.SendAsync("RoomCreated", new { room, success = true });
                await Clients.Group("RoomList").SendAsync("RoomUpdated", room);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CreateRoom for ConnectionId: {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", $"can't create room: {ex.Message}");
            }
        }

        // 4. Join Room
        public async Task JoinRoom(string roomId)
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                _logger.LogInformation("➡️ JoinRoom {RoomId} called by {PlayerId} - {PlayerName}", roomId, playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized JoinRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    await Clients.Caller.SendAsync("Error", "Unauthorized: Invalid token");
                    return;
                }

                var room = await _roomService.GetRoomById(roomId);
                if (room == null)
                {
                    _logger.LogWarning("⚠️ Room {RoomId} not found. Player: {PlayerId}", roomId, playerId);
                    await Clients.Caller.SendAsync("Error", "Room not found");
                    return;
                }
                if (room.Status != RoomStatus.Waiting)
                {
                    _logger.LogWarning("⚠️ Room {RoomId} already started. Player: {PlayerId}", roomId, playerId);
                    await Clients.Caller.SendAsync("Error", "Game has already started in this room");
                    return;
                }
                if (room.CurrentPlayers >= room.QuantityPlayer)
                {
                    _logger.LogWarning("⚠️ Room {RoomId} is full. Player: {PlayerId}", roomId, playerId);
                    await Clients.Caller.SendAsync("Error", "Room is full");
                    return;
                }
                if (room.Players.Any(p => p.PlayerId == playerId))
                {
                    _logger.LogWarning("⚠️ Player {PlayerId} already in Room {RoomId}", playerId, roomId);
                    await Clients.Caller.SendAsync("Error", "You are already in this room");
                    return;
                }

                await _roomService.JoinRoom(roomId, playerId, playerName);
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");

                var updatedRoom = await _roomService.GetRoomById(roomId);

                _logger.LogInformation("✅ Player {PlayerId} joined Room {RoomId}. Current players: {Count}", playerId, roomId, updatedRoom?.CurrentPlayers);

                await Clients.Caller.SendAsync("JoinedRoom", new { room = updatedRoom, success = true });
                await Clients.GroupExcept($"Room_{roomId}", Context.ConnectionId)
                    .SendAsync("PlayerJoined", new { playerId, playerName, room = updatedRoom });

                await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in JoinRoom {RoomId} for Player {PlayerId}", roomId, Context.User?.Identity?.Name);
                await Clients.Caller.SendAsync("Error", $"can't join room: {ex.Message}");
            }
        }
    }
}

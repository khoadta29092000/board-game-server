using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.Room;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;

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

        private async Task AutoLeaveRoom()
        {
            var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);

            if (userConnection != null && !string.IsNullOrEmpty(userConnection.RoomId))
            {
                _logger.LogInformation("🚪 Auto removing player {PlayerId} from room {RoomId} due to disconnect",
                    userConnection.PlayerId, userConnection.RoomId);

                var updatedRoom = await _roomService.LeaveRoom(userConnection.RoomId, userConnection.PlayerId);

                if (updatedRoom != null)
                {
                    await Clients.Group($"Room_{userConnection.RoomId}")
                        .SendAsync("PlayerLeft", new
                        {
                            playerId = userConnection.PlayerId,
                            room = updatedRoom,
                            reason = "disconnected"
                        });

                    if (updatedRoom.CurrentPlayers > 0)
                    {
                        await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);
                    }
                    else
                    {
                        await Clients.Group("RoomList").SendAsync("RoomRemoved", userConnection.RoomId);
                    }

                    _logger.LogInformation("✅ Player {PlayerId} auto-removed from room {RoomId}. Remaining players: {Count}",
                        userConnection.PlayerId, userConnection.RoomId, updatedRoom.CurrentPlayers);
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                _logger.LogWarning("❌ Client disconnected: {ConnectionId}, Reason: {Reason}",
                    Context.ConnectionId, exception?.Message);

                await AutoLeaveRoom();

                await _userConnectionService.RemoveConnection(Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling disconnect for {ConnectionId}", Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

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

        public async Task GetActiveRooms()
        {
            try
            {
                _logger.LogInformation("📥 GetActiveRooms called by {ConnectionId}", Context.ConnectionId);

                await AutoLeaveRoom();

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

        public async Task<object> CreateRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                string Id = ObjectId.GenerateNewId().ToString();
                _logger.LogInformation("➡️ CreateRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized JoinRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    throw new HubException("Unauthorized: Invalid token");
                }

                var room = new Room
                {
                    RoomId = "room_" + playerName,
                    CurrentPlayers = 1,
                    QuantityPlayer = 4,
                    Id = Id,
                    Players = new List<RoomPlayer>
                    {
                        new RoomPlayer { IsOwner = true, Name = playerName!, PlayerId = playerId! }
                    },
                    RoomType = RoomType.Public,
                    Status = RoomStatus.Waiting
                };

                var createdRoom = await _roomService.CreateRoom(room);
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, createdRoom.Id);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{createdRoom.Id}");

                _logger.LogInformation("✅ Room created: {RoomId} by {PlayerName}", createdRoom.Id, playerName);

                await Clients.Group("RoomList").SendAsync("RoomUpdated", createdRoom);
                return new { room = createdRoom, success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CreateRoom for ConnectionId: {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", $"can't create room: {ex.Message}");
                throw new HubException($"Can't create room: {ex.Message}");
            }
        }

        public async Task<object> JoinRoom(string roomId)
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                _logger.LogInformation("➡️ JoinRoom {RoomId} called by {PlayerId} - {PlayerName}",
                    roomId, playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized JoinRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    await Clients.Caller.SendAsync("JoinRoomError", new { error = "Unauthorized: Invalid token" });
                    return new { success = false, error = "Unauthorized" };
                }

                var room = await _roomService.GetRoomById(roomId);
                if (room == null)
                {
                    _logger.LogWarning("⚠️ Room {RoomId} not found. Player: {PlayerId}", roomId, playerId);
                    await Clients.Caller.SendAsync("JoinRoomError", new { error = "Room not found" });
                    return new { success = false, error = "Room not found" };
                }

                if (room.Status != RoomStatus.Waiting)
                {
                    await Clients.Caller.SendAsync("JoinRoomError", new { error = "Game has already started" });
                    return new { success = false, error = "Game has already started" };
                }

                // Kiểm tra player đã trong phòng chưa TRƯỚC
                RoomPlayer? existingPlayer = room.Players?.Find(p => p.PlayerId == playerId);
                if (existingPlayer != null)
                {
                    // Player đã trong phòng - chỉ reconnect
                    await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");
                    await Clients.Caller.SendAsync("JoinedRoom", new { room, success = true });
                    return new { room, success = true };
                }

                // Kiểm tra room full SAU khi đã check existing player
                if (room.CurrentPlayers >= room.QuantityPlayer)
                {
                    await Clients.Caller.SendAsync("JoinRoomError", new { error = "Room is full" });
                    return new { success = false, error = "Room is full" };
                }

                // Join room - KHÔNG GỬI ERROR Ở ĐÂY NỮA
                Room? updatedRoom = await _roomService.JoinRoom(roomId, playerId, playerName);

                if (updatedRoom == null)
                {
                    // Race condition - room became full
                    await Clients.Caller.SendAsync("JoinRoomError", new { error = "Room is full" });
                    return new { success = false, error = "Room is full" };
                }

                // Join thành công
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, updatedRoom.Id);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{updatedRoom.Id}");

                _logger.LogInformation("✅ Player {PlayerId} joined Room {RoomId}. Current players: {Count}",
                    playerId, roomId, updatedRoom.CurrentPlayers);
                // Success notifications
                await Clients.Caller.SendAsync("JoinedRoom", new { room = updatedRoom, success = true });
                await Clients.GroupExcept($"Room_{roomId}", Context.ConnectionId)
                    .SendAsync("PlayerJoined", new { playerId, playerName, room = updatedRoom });
                await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);

                return new { room = updatedRoom, success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in JoinRoom {RoomId} for Player {PlayerId}",
                    roomId, Context.User?.Identity?.Name);

                // CHỈ GỬI 1 LẦN ERROR
                await Clients.Caller.SendAsync("JoinRoomError", new { error = "Cannot join room" });
                return new { success = false, error = "Cannot join room" };
            }
        }



        public async Task<object> LeaveRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                _logger.LogInformation("↩️ LeaveRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized LeaveRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    throw new HubException("Unauthorized: Invalid token");
                }

                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection == null || string.IsNullOrEmpty(userConnection.RoomId))
                {
                    _logger.LogWarning("⚠️ Player {PlayerId} not in any room", playerId);
                    throw new HubException("You are not in any room");
                }

                var roomId = userConnection.RoomId;

                var updatedRoom = await _roomService.LeaveRoom(roomId, playerId);

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Room_{roomId}");

                await _userConnectionService.RemoveUserFromRoom(playerId, roomId);

                _logger.LogInformation("✅ Player {PlayerId} left Room {RoomId}. Remaining players: {Count}",
                    playerId, roomId, updatedRoom?.CurrentPlayers ?? 0);

                await Clients.Caller.SendAsync("LeftRoom", new { success = true, roomId });

                if (updatedRoom != null && updatedRoom.CurrentPlayers > 0)
                {
                    await Clients.Group($"Room_{roomId}")
                        .SendAsync("PlayerLeft", new
                        {
                            playerId,
                            playerName,
                            room = updatedRoom,
                            reason = "manual"
                        });

                    await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);
                    return new { room = updatedRoom, success = true };
                }
                else
                {
                    await Clients.Group("RoomList").SendAsync("RoomRemoved", roomId);
                    return new { room = roomId, success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in LeaveRoom for Player {PlayerId}", Context.User?.Identity?.Name);
                throw new HubException($"can't leave roo: {ex.Message}");
            }
        }

        public async Task GetCurrentRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();

                if (playerId == null)
                {
                    await Clients.Caller.SendAsync("CurrentRoomInfo", new { room = (Room?)null, success = false });
                    return;
                }

                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection == null || string.IsNullOrEmpty(userConnection.RoomId))
                {
                    await Clients.Caller.SendAsync("CurrentRoomInfo", new { room = (Room?)null, success = true });
                    return;
                }

                var room = await _roomService.GetRoomById(userConnection.RoomId);
                await Clients.Caller.SendAsync("CurrentRoomInfo", new { room, success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting current room for {PlayerId}", Context.User?.Identity?.Name);
                await Clients.Caller.SendAsync("Error", $"can't get current room: {ex.Message}");
            }
        }
    }
}
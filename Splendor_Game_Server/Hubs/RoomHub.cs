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

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                _logger.LogWarning("❌ Client disconnected: {ConnectionId}, Reason: {Reason}",
                    Context.ConnectionId, exception?.Message);

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

        public async Task CreateRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                string Id = ObjectId.GenerateNewId().ToString();
                _logger.LogInformation("➡️ CreateRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized CreateRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    await Clients.Caller.SendAsync("Error", "Unauthorized: Invalid token");
                    return;
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

                await Clients.Caller.SendAsync("RoomCreated", new { room = createdRoom, success = true });
                await Clients.Group("RoomList").SendAsync("RoomUpdated", createdRoom);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in CreateRoom for ConnectionId: {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", $"can't create room: {ex.Message}");
            }
        }

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

                Room updatedRoom = await _roomService.JoinRoom(roomId, playerId, playerName);
                await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");

                _logger.LogInformation("✅ Player {PlayerId} joined Room {RoomId}. Current players: {Count}",
                    playerId, roomId, updatedRoom?.CurrentPlayers);

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

        public async Task LeaveRoom()
        {
            try
            {
                var (playerId, playerName) = GetPlayerInfo();
                _logger.LogInformation("↩️ LeaveRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                if (playerId == null)
                {
                    _logger.LogWarning("⚠️ Unauthorized LeaveRoom attempt. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    await Clients.Caller.SendAsync("Error", "Unauthorized: Invalid token");
                    return;
                }

                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection == null || string.IsNullOrEmpty(userConnection.RoomId))
                {
                    _logger.LogWarning("⚠️ Player {PlayerId} not in any room", playerId);
                    await Clients.Caller.SendAsync("Error", "You are not in any room");
                    return;
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
                }
                else
                {
                    await Clients.Group("RoomList").SendAsync("RoomRemoved", roomId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in LeaveRoom for Player {PlayerId}", Context.User?.Identity?.Name);
                await Clients.Caller.SendAsync("Error", $"can't leave room: {ex.Message}");
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
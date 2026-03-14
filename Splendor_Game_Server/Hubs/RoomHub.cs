using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.Model.Player;
using CleanArchitecture.Domain.Model.Room;
using CleanArchitecture.Infrastructure.Redis;
using GraphQLParser;
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
        private readonly ISplendorService _gameService;
        private readonly IRedisMapper _redisMapper;

        public RoomHub(IRoomService roomService, IUserConnectionService userConnectionService, ILogger<RoomHub> logger, ISplendorService gameService, IRedisMapper redisMapper)
        {
            _roomService = roomService;
            _userConnectionService = userConnectionService;
            _logger = logger;
            _gameService = gameService;
            _redisMapper = redisMapper;
        }

        private (string? playerId, string? playerName) GetPlayerInfo()
        {
            var playerId = Context.User.FindFirst("Id")?.Value;
            var playerName = Context.User.FindFirst("Name")?.Value;
            return (playerId, playerName);
        }

        private async Task<(string playerId, string playerName)> GetValidatedPlayerInfo()
        {
            var (playerId, playerName) = GetPlayerInfo();
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(playerName))
            {
                throw new HubException("Unauthorized: Invalid token");
            }
            return (playerId!, playerName!);
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

                await AutoLeaveRoom();
                await _userConnectionService.RemoveConnection(Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling disconnect for {ConnectionId}", Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        private async Task AutoLeaveRoom()
        {
            var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
            if (userConnection?.RoomId != null)
            {
                _logger.LogInformation("🚪 Auto removing player {PlayerId} from room {RoomId} due to disconnect",
                    userConnection.PlayerId, userConnection.RoomId);

                // Get player name for disconnect notification
                var (_, playerName) = GetPlayerInfo();
                await HandlePlayerLeaving(userConnection.RoomId, userConnection.PlayerId, "disconnected", playerName);
            }
        }

        public async Task JoinRoomListGroup()
        {
            try
            {
                _logger.LogInformation("➡️ {ConnectionId} joined RoomList group", Context.ConnectionId);
                await Groups.AddToGroupAsync(Context.ConnectionId, "RoomList");
                _logger.LogInformation("✅ {ConnectionId} successfully joined RoomList group", Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining RoomList group for {ConnectionId}", Context.ConnectionId);
                throw;
            }
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

                var rooms = await _roomService.GetActiveRooms();
                _logger.LogInformation("📦 Found {RoomCount} active rooms", rooms.Count);

                await Clients.Caller.SendAsync("ActiveRoomsLoaded", rooms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active rooms. ConnectionId: {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Failed to load rooms");
            }
        }

        public async Task<object> CreateRoom()
        {
            try
            {
                var (playerId, playerName) = await GetValidatedPlayerInfo();
                string roomId = ObjectId.GenerateNewId().ToString();

                _logger.LogInformation("➡️ CreateRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                var room = new Room
                {
                    RoomId = "room_" + playerName,
                    CurrentPlayers = 1,
                    QuantityPlayer = 4,
                    Id = roomId,
                    Players = new List<RoomPlayer>
                    {
                        new RoomPlayer { IsOwner = true, Name = playerName, PlayerId = playerId, isReady = false }
                    },
                    RoomType = RoomType.Public,
                    Status = RoomStatus.Waiting
                };

                var createdRoom = await _roomService.CreateRoom(room);
                await HandlePlayerJoining(createdRoom, playerId);

                _logger.LogInformation("✅ Room created: {RoomId} by {PlayerName}", createdRoom.Id, playerName);

                await Clients.Group("RoomList").SendAsync("RoomUpdated", createdRoom);
                return new { room = createdRoom, success = true };
            }
            catch (Exception ex)
            {
                return await HandleHubError(ex, "Failed to create room");
            }
        }

        public async Task<object> JoinRoom(string roomId)
        {
            try
            {
                var (playerId, playerName) = await GetValidatedPlayerInfo();
                _logger.LogInformation("➡️ JoinRoom {RoomId} called by {PlayerId} - {PlayerName}",
                    roomId, playerId, playerName);

                var room = await _roomService.JoinRoom(roomId, playerId, playerName);
            
                await HandlePlayerJoining(room, playerId);

                await Clients.GroupExcept($"Room_{roomId}", Context.ConnectionId)
                     .SendAsync("PlayerJoined", new { playerId, playerName, room });

                await Clients.Group("RoomList").SendAsync("RoomUpdated", room);

                _logger.LogInformation("✅ Player {PlayerId} joined Room {RoomId}. Current players: {Count}",
                    playerId, roomId, room.CurrentPlayers);

                return new { room, success = true };
            }
            catch (Exception ex)
            {
                return await HandleHubError(ex, "Failed to join room");
            }
        }

        public async Task<object> LeaveRoom()
        {
            try
            {
                var (playerId, playerName) = await GetValidatedPlayerInfo();
                _logger.LogInformation("↩️ LeaveRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection?.RoomId == null)
                {
                    throw new InvalidOperationException("You are not in any room");
                }

                var roomId = userConnection.RoomId;
                var updatedRoom = await HandlePlayerLeaving(roomId, playerId, "manual", playerName);

                await Clients.Caller.SendAsync("LeftRoom", new { success = true, roomId });

                _logger.LogInformation("✅ Player {PlayerId} left Room {RoomId}", playerId, roomId);

                // Return appropriate response based on room state
                if (updatedRoom != null && updatedRoom.CurrentPlayers > 0)
                {
                    return new { room = updatedRoom, success = true };
                }
                else
                {
                    return new { roomId = roomId, success = true, roomDeleted = true };
                }
            }
            catch (Exception ex)
            {
                return await HandleHubError(ex, "Failed to leave room");
            }
        }

        public async Task<object> PlayerChangeReady(bool isReady)
        {
            try
            {
                var (playerId, playerName) = await GetValidatedPlayerInfo();
                _logger.LogInformation("↩️ LeaveRoom called by {PlayerId} - {PlayerName}", playerId, playerName);

                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection?.RoomId == null)
                {
                    throw new InvalidOperationException("You are not in any room");
                }

                var roomId = userConnection.RoomId;

                var updatedRoom = await _roomService.PlayerisReady(roomId, playerId, isReady);

                _logger.LogInformation("✅ Player change ready for room {RoomId}", roomId);

                await Clients.GroupExcept($"Room_{roomId}", Context.ConnectionId)
                   .SendAsync("PlayerChangeReady", new { updatedRoom });
                await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);

                return new { room = updatedRoom, success = true };
            }
            catch (Exception ex)
            {
                return await HandleHubError(ex, "Failed to change ready settings");
            }
        }
        public async Task<object> StartGame()
        {
            try
            {
                var (playerId, _) = await GetValidatedPlayerInfo();
                _logger.LogInformation("🎮 StartGame called for room by player {PlayerId}", playerId);
                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection?.RoomId == null)
                {
                    throw new InvalidOperationException("You are not in any room");
                }

                var roomId = userConnection.RoomId;


                Room startedRoom = await _roomService.StartGame(roomId, playerId);

                var context = await _gameService.StartGameAsync(roomId, startedRoom.Players);

                // 4. Sync state sang Redis
                await _redisMapper.SyncGameStateToRedis(context, roomId);

                _logger.LogInformation("✅ Game started in room {RoomId}. Players: {Count}",
                    roomId, startedRoom.CurrentPlayers);

                await Clients.Group($"Room_{roomId}").SendAsync("GameStarted", new { startedRoom, roomId});
                await Clients.Group("RoomList").SendAsync("RoomUpdated", startedRoom);
                //await Clients.Group($"game:{roomId}").SendAsync("GameStarted", new
                //{
                //    gameId = roomId,
                //    players = startedRoom.Players
                //});

                //remove all players from room group since game has started
                foreach (var player in startedRoom.Players)
                {
                    var userConn = await _userConnectionService.GetUserByConnection(player.PlayerId);
                    if (userConn?.ConnectionId != null)
                    {
                        _logger.LogInformation("🚪 Auto removing player {PlayerId} from room {RoomId} due to disconnect",
                            userConn.PlayerId, userConn.RoomId);

                        await Groups.RemoveFromGroupAsync(userConn.ConnectionId, $"Room_{roomId}");
                    }
                }

                await _userConnectionService.RemoveConnection(Context.ConnectionId);
                return new { success = true, startedRoom, roomId };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Hub error starting game for room");
                return await HandleHubError(ex, "Failed to start game");
            }
        }

        public async Task<object> UpdateRoomSettings(int? maxPlayers, RoomType? roomType)
        {
            try
            {
                var (playerId, _) = await GetValidatedPlayerInfo();
                _logger.LogInformation("⚙️ UpdateRoomSettings called for room {RoomId} by player {PlayerId}. MaxPlayers: {MaxPlayers}, RoomType: {RoomType}",
                    playerId, maxPlayers, roomType);
                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection?.RoomId == null)
                {
                    throw new InvalidOperationException("You are not in any room");
                }

                var roomId = userConnection.RoomId;

                var updatedRoom = await _roomService.UpdateRoomSettings(roomId, playerId, maxPlayers, roomType);

                _logger.LogInformation("✅ Room settings updated for room {RoomId}", roomId);

                await Clients.GroupExcept($"Room_{roomId}", Context.ConnectionId)
                   .SendAsync("RoomSettingsUpdated", new { maxPlayers, roomType });
                await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);

                return new { room = updatedRoom, success = true };
            }
            catch (Exception ex)
            {
                return await HandleHubError(ex, "Failed to update room settings");
            }
        }

        private async Task HandlePlayerJoining(Room room, string playerId)
        {
            await LeaveRoomListGroup();
            await _userConnectionService.AddUserConnection(playerId, Context.ConnectionId, room.Id);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Room_{room.Id}");
            await Clients.Caller.SendAsync("JoinedRoom", new { room, success = true });
        }

        private async Task<Room?> HandlePlayerLeaving(string roomId, string playerId, string reason, string? playerName = null)
        {
            try
            {
                var updatedRoom = await _roomService.LeaveRoom(roomId, playerId);

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Room_{roomId}");
                await _userConnectionService.RemoveUserFromRoom(playerId, roomId);
                if (updatedRoom != null && updatedRoom.CurrentPlayers > 0)
                {
                    // Room still has players - notify them
                    await Clients.Group($"Room_{roomId}")
                        .SendAsync("PlayerLeft", new
                        {
                            playerId,
                            playerName,
                            room = updatedRoom,
                            reason
                        });
                    await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);

                    _logger.LogInformation("✅ Player {PlayerId} left room {RoomId}. Remaining players: {Count}",
                        playerId, roomId, updatedRoom.CurrentPlayers);
                }
                else
                {
                    // Room is empty - remove from room list
                    await Clients.Group("RoomList").SendAsync("RoomRemoved", roomId);
                    _logger.LogInformation("✅ Room {RoomId} deleted after last player left", roomId);
                }
                await JoinRoomListGroup();

                return updatedRoom;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling player leaving room {RoomId}", roomId);
                throw;
            }
        }
        public async Task<object> AddBotToRoom()
        {
            try
            {
                var (playerId, _) = await GetValidatedPlayerInfo();

                var userConnection = await _userConnectionService.GetUserByConnection(Context.ConnectionId);
                if (userConnection?.RoomId == null)
                    throw new InvalidOperationException("You are not in any room");

                var roomId = userConnection.RoomId;
                var room = await _roomService.GetRoomById(roomId);
                if (room == null) throw new InvalidOperationException("Room not found");

                // Chỉ owner mới được add bot
                var owner = room.Players?.FirstOrDefault(p => p.IsOwner);
                if (owner?.PlayerId != playerId)
                    throw new UnauthorizedOperationException("Only room owner can add a bot");

                var botId = $"BOT_{Guid.NewGuid():N}"; 
                var botName = "AI Bot";

                // Kiểm tra bot chưa có trong room
                if (room.Players?.Any(p => p.PlayerId.StartsWith("BOT_")) == true)
                    return new { success = false, error = "Bot already in room" };

                // Add bot vào room (dùng JoinRoom của service)
                var updatedRoom = await _roomService.AddBotToRoom(roomId, botId, botName);

                _logger.LogInformation("🤖 Bot {BotId} added to room {RoomId}", botId, roomId);

                await Clients.Group($"Room_{roomId}").SendAsync("PlayerJoined", new
                {
                    playerId = botId,
                    playerName = botName,
                    room = updatedRoom,
                    isBot = true
                });
                await Clients.Group("RoomList").SendAsync("RoomUpdated", updatedRoom);

                return new { success = true, room = updatedRoom };
            }
            catch (Exception ex)
            {
                return await HandleHubError(ex, "Failed to add bot to room");
            }
        }
        private async Task<object> HandleHubError(Exception ex, string defaultMessage)
        {
            var errorMessage = ex switch
            {
                RoomNotFoundException => "Room not found",
                RoomFullException => "Room is full",
                InvalidRoomStatusException => "Game has already started",
                UnauthorizedOperationException => ex.Message,
                InsufficientPlayersException => ex.Message,
                PlayerNotFoundException => "Player not found in room",
                HubException => ex.Message,
                InvalidOperationException => ex.Message,
                ArgumentException => ex.Message,
                _ => defaultMessage
            };

            _logger.LogError(ex, "Hub error: {Message}", errorMessage);
            //await Clients.Caller.SendAsync("Error", errorMessage);
            return new { success = false, error = errorMessage };
        }

    }
}
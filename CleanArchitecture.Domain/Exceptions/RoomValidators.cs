using CleanArchitecture.Domain.Model.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Exceptions
{
    public static class RoomValidators
    {
        public static void ValidateRoomExists(Room? room, string roomId)
        {
            if (room == null)
                throw new RoomNotFoundException(roomId);
        }

        public static void ValidateRoomStatus(Room room, RoomStatus expectedStatus)
        {
            if (room.Status != expectedStatus)
                throw new InvalidRoomStatusException(room.Status, expectedStatus);
        }

        public static void ValidateRoomCapacity(Room room)
        {
            if (room.CurrentPlayers >= room.QuantityPlayer)
                throw new RoomFullException(room.Id);
        }

        public static void ValidateMinimumPlayers(Room room, int minimumPlayers = 2)
        {
            if (room.CurrentPlayers < minimumPlayers)
                throw new InsufficientPlayersException(room.CurrentPlayers, minimumPlayers);
        }

        public static void ValidateRoomOwnership(Room room, string playerId)
        {
            var owner = room.Players?.FirstOrDefault(p => p.IsOwner);
            if (owner?.PlayerId != playerId)
                throw new UnauthorizedOperationException("Only room owner can perform this action");
        }

        public static void ValidatePlayerInRoom(Room room, string playerId)
        {
            var player = room.Players?.FirstOrDefault(p => p.PlayerId == playerId);
            if (player == null)
                throw new PlayerNotFoundException(playerId, room.Id);
        }

        public static void ValidateMaxPlayersUpdate(Room room, int newMaxPlayers)
        {
            if (newMaxPlayers < room.CurrentPlayers)
                throw new InvalidOperationException("Cannot set max players below current player count");
        }

        public static bool IsPlayerInRoom(Room room, string playerId)
        {
            return room.Players?.Any(p => p.PlayerId == playerId) ?? false;
        }
        public static void ValidateAllPlayersReady(Room room)
        {
            if (room.Players == null || !room.Players.Any())
            {
                throw new InvalidOperationException("Room has no players");
            }

            var notReadyPlayers = room.Players
          .Where(p => !p.IsOwner && !p.isReady)
          .ToList();

            if (notReadyPlayers.Any())
            {
                var notReadyNames = string.Join(", ", notReadyPlayers.Select(p => p.Name));
                throw new InvalidOperationException(
                    $"Cannot start game. The following players are not ready: {notReadyNames}"
                );
            }
        }
    }
}

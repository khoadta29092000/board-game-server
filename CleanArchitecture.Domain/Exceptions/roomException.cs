using CleanArchitecture.Domain.Model.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Exceptions
{
    public class RoomNotFoundException : Exception
    {
        public RoomNotFoundException(string roomId)
            : base($"Room '{roomId}' not found") { }
    }

    public class RoomFullException : Exception
    {
        public RoomFullException(string roomId)
            : base($"Room '{roomId}' is full") { }
    }

    public class InvalidRoomStatusException : Exception
    {
        public InvalidRoomStatusException(RoomStatus currentStatus, RoomStatus expectedStatus)
            : base($"Room status is {currentStatus}, expected {expectedStatus}") { }
    }

    public class UnauthorizedOperationException : Exception
    {
        public UnauthorizedOperationException(string operation)
            : base($"Unauthorized to perform operation: {operation}") { }
    }

    public class InsufficientPlayersException : Exception
    {
        public InsufficientPlayersException(int currentPlayers, int requiredPlayers)
            : base($"Need at least {requiredPlayers} players to start, currently have {currentPlayers}") { }
    }
    public class PlayerNotFoundException : Exception
    {
        public PlayerNotFoundException(string playerId, string roomId)
            : base($"Player '{playerId}' not found in room '{roomId}'") { }
    }
}

using CleanArchitecture.Domain.Model.Splendor.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Exceptions
{
    public class SplendorException : Exception
    {
        public string ErrorCode { get; }

        public SplendorException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }
    }

    public class GameNotFoundException : SplendorException
    {
        public GameNotFoundException(string roomCode)
            : base("GAME_NOT_FOUND", $"Game with room code '{roomCode}' not found")
        {
        }
    }

    public class GameNotInProgressException : SplendorException
    {
        public GameNotInProgressException(string roomCode)
            : base("GAME_NOT_IN_PROGRESS", $"Game '{roomCode}' is not in progress")
        {
        }
    }

    public class NotYourTurnException : SplendorException
    {
        public NotYourTurnException(string playerId, string currentPlayerId)
            : base("NOT_YOUR_TURN", $"It's not your turn. Current player: {currentPlayerId}")
        {
        }
    }

    public class InvalidTurnPhaseException : SplendorException
    {
        public InvalidTurnPhaseException(TurnPhase currentPhase, TurnPhase expectedPhase)
            : base("INVALID_TURN_PHASE", $"Invalid turn phase. Current: {currentPhase}, Expected: {expectedPhase}")
        {
        }
    }

    public class InvalidGemCollectionException : SplendorException
    {
        public InvalidGemCollectionException(string reason)
            : base("INVALID_GEM_COLLECTION", $"Cannot collect gems: {reason}")
        {
        }
    }
}

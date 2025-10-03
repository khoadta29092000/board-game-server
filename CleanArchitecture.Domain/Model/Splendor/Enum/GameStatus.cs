using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.Splendor.Enum
{
    public enum GameStatus
    {
        Pending,      // Đang chờ người chơi join
        InProgress,   // Đang chơi
        Completed     // Đã kết thúc
    }
}

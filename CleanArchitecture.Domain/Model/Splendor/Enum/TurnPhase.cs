using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.Splendor.Enum
{
    public enum TurnPhase
    {
        WaitingForAction,   // Chờ player bắt đầu hành động (lượt của người đó nhưng chưa chọn gì)
        SelectingGems,      // Player đang chọn gem để lấy
        ProcessingNobles,   // Xử lý điều kiện noble (xem có noble nào được nhận không)
        Completed,           // Lượt đã hoàn thành, chuyển sang player kế tiếp
        SelectingNoble // Chọn noble sau khi mua card
    }
}

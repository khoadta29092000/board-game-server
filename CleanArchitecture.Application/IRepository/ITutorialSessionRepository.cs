namespace CleanArchitecture.Application.IRepository
{
    /// <summary>
    /// Repository để quản lý trạng thái disconnect/reconnect và step của tutorial session.
    /// Thuộc Application layer — implementation nằm ở Infrastructure.
    /// </summary>
    public interface ITutorialSessionRepository
    {
        /// <summary>
        /// Ghi nhận player vừa disconnect. Lưu timestamp để tính grace period.
        /// </summary>
        Task MarkDisconnectedAsync(string playerId);

        /// <summary>
        /// Xóa disconnect mark khi player reconnect trong grace period.
        /// </summary>
        Task ClearDisconnectMarkAsync(string playerId);

        /// <summary>
        /// Lấy danh sách tất cả playerId đang trong trạng thái disconnect.
        /// </summary>
        Task<List<string>> GetDisconnectedPlayerIdsAsync();

        /// <summary>
        /// Lấy thời điểm disconnect của player. Trả về null nếu không có.
        /// </summary>
        Task<DateTimeOffset?> GetDisconnectTimeAsync(string playerId);

        /// <summary>
        /// Xóa toàn bộ data disconnect của player (timestamp + khỏi danh sách).
        /// </summary>
        Task RemoveDisconnectDataAsync(string playerId);

        /// <summary>
        /// Lưu step + phase hiện tại của tutorial.
        /// </summary>
        Task SaveStepAsync(string playerId, int stepIndex, string phase);

        /// <summary>
        /// Đọc step đã lưu. Trả về null nếu không có.
        /// </summary>
        Task<(int stepIndex, string phase)?> LoadStepAsync(string playerId);

        /// <summary>
        /// Xóa step data (khi tutorial kết thúc hoặc cleanup).
        /// </summary>
        Task DeleteStepAsync(string playerId);
    }
}
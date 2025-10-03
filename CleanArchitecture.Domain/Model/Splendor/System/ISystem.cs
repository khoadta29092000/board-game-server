using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.Components;

namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public interface ISystem
    {
        void Execute(GameContext context);
    }

    public class GameContext
    {
        public Dictionary<Guid, SplendorEntities> Entities { get; set; }
        public GameSession GameSession { get; set; }

        public GameContext()
        {
            Entities = new Dictionary<Guid, SplendorEntities>();
        }

        public T? GetEntity<T>(Guid id) where T : SplendorEntities
        {
            return Entities.TryGetValue(id, out var entity) ? entity as T : null;
        }

        // trả về list entity có component T
        public IEnumerable<SplendorEntities> GetEntitiesWithComponent<T>() where T : class, IComponent
        {
            return Entities.Values.Where(e => e.HasComponent<T>());
        }

        // Lấy component từ entity id trực tiếp
        public T? GetComponentFromEntity<T>(Guid entityId) where T : class, IComponent
        {
            var e = GetEntity<SplendorEntities>(entityId);
            return e?.GetComponent<T>();
        }
    }
}

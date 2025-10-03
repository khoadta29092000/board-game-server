using CleanArchitecture.Domain.Model.Splendor.Components;

namespace CleanArchitecture.Domain.Model.Splendor.Entity
{
    public abstract class SplendorEntities
    {
        public Guid Id { get; protected set; }
        private List<IComponent> _components = new();

        protected SplendorEntities()
        {
            Id = Guid.NewGuid();
        }

        public void AddComponent(IComponent component)
        {
            _components.Add(component);
        }

        public T? GetComponent<T>() where T : class, IComponent
        {
            return _components.OfType<T>().FirstOrDefault();
        }

        public IEnumerable<IComponent> GetComponents() => _components;

        public bool HasComponent<T>() where T : IComponent
        {
            return _components.OfType<T>().Any();
        }

        public void RemoveComponent<T>() where T : IComponent
        {
            var component = _components.OfType<T>().FirstOrDefault();
            if (component != null)
                _components.Remove(component);
        }
    }
}

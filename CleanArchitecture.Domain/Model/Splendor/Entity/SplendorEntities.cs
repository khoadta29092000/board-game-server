using CleanArchitecture.Domain.Model.Splendor.Components;
using System.Text.Json.Serialization;

namespace CleanArchitecture.Domain.Model.Splendor.Entity
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(GameSession), "gameSession")]
    [JsonDerivedType(typeof(PlayerEntity), "playerEntity")]
    [JsonDerivedType(typeof(CardEntity), "cardEntity")]
    [JsonDerivedType(typeof(NobleEntity), "nobleEntity")]
    [JsonDerivedType(typeof(BoardEntity), "boardEntity")]

    public abstract class SplendorEntities
    {
        public Guid Id { get; protected set; }

        [JsonInclude]
        public List<IComponent> Components { get; private set; } = new();

        protected SplendorEntities()
        {
            Id = Guid.NewGuid();
        }

        public void AddComponent(IComponent component)
        {
            Components.Add(component);
        }

        public T? GetComponent<T>() where T : class, IComponent
        {
            return Components.OfType<T>().FirstOrDefault();
        }

        public IEnumerable<IComponent> GetComponents() => Components;

        public bool HasComponent<T>() where T : IComponent
        {
            return Components.OfType<T>().Any();
        }

        public void RemoveComponent<T>() where T : IComponent
        {
            var component = Components.OfType<T>().FirstOrDefault();
            if (component != null)
                Components.Remove(component);
        }
    }
}

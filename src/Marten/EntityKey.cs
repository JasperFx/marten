using System;

namespace Marten
{
    internal class EntityKey : IEquatable<EntityKey>
    {
        public object Id { get; }
        public Type EntityType { get; }

        public EntityKey(Type entityType, object id)
        {
            EntityType = entityType;
            Id = id;
        }

        public bool Equals(EntityKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id.Equals(other.Id) && Equals(EntityType, other.EntityType);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((EntityKey) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Id.GetHashCode()*397) ^ (EntityType != null ? EntityType.GetHashCode() : 0);
            }
        }
    }
}
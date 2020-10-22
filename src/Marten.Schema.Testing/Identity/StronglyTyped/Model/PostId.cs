using System;

namespace Marten.Schema.Testing.Identity.StronglyTyped.Model
{
    public struct PostId
    {
        public Guid Id { get; }

        public PostId(Guid id)
        {
            Id = id;
        }
        public bool Equals(PostId other)
        {
            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            return obj is PostId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static bool operator ==(PostId a, PostId b)
        {
            return a.Id == b.Id;
        }

        public static bool operator !=(PostId a, PostId b)
        {
            return !(a == b);
        }
    }
}

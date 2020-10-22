using System;

namespace Marten.Schema.Testing.Identity.StronglyTyped.Model
{
    public struct BlogId
    {
        public Guid Id { get; }

        public BlogId(Guid id)
        {
            Id = id;
        }
        public bool Equals(BlogId other)
        {
            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            return obj is BlogId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static bool operator ==(BlogId a, BlogId b)
        {
            return a.Id == b.Id;
        }

        public static bool operator !=(BlogId a, BlogId b)
        {
            return !(a == b);
        }
    }
}

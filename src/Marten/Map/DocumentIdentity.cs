using System;

namespace Marten.Map
{
    public class DocumentIdentity : IEquatable<DocumentIdentity>
    {
        public object Id { get; }
        public Type DocumentType { get; }

        public DocumentIdentity(Type documentType, object id)
        {
            DocumentType = documentType;
            Id = id;
        }

        public bool Equals(DocumentIdentity other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id.Equals(other.Id) && Equals(DocumentType, other.DocumentType);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DocumentIdentity) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Id.GetHashCode()*397) ^ (DocumentType?.GetHashCode() ?? 0);
            }
        }

        public override string ToString()
        {
            return $"Id: {Id}, DocumentType: {DocumentType}";
        }
    }
}
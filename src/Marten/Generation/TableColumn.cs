using System;
using System.Diagnostics.Contracts;
using Marten.Util;

namespace Marten.Generation
{
    public class TableColumn
    {
        public string Name { get; }
        public string Type { get; }
        public string Directive { get; set; }

        public TableColumn(string name, string type)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentOutOfRangeException(nameof(name));
            if (string.IsNullOrEmpty(type)) throw new ArgumentOutOfRangeException(nameof(type));
            Name = name.ToLower();
            Type = type.ToLower();
        }

        protected bool Equals(TableColumn other)
        {
            return string.Equals(Name, other.Name) && string.Equals(TypeMappings.ConvertSynonyms(Type), TypeMappings.ConvertSynonyms(other.Type));
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TableColumn) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Name.GetHashCode() * 397) ^ (Type.GetHashCode());
            }
        }
    }
}
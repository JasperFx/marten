using System;
using Marten.Util;

namespace Marten.Generation
{
    public class TableColumn
    {
        public TableColumn(string name, string type)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentOutOfRangeException(nameof(name));
            if (string.IsNullOrEmpty(type)) throw new ArgumentOutOfRangeException(nameof(type));
            Name = name.ToLower();
            Type = type.ToLower();
        }

        public string Name { get; }

        // Needs to be writeable here.
        public string Type { get; set; }

        public string RawType()
        {
            return Type.Split('(')[0].Trim();
        }


        public string Directive { get; set; }

        protected bool Equals(TableColumn other)
        {
            return string.Equals(Name, other.Name) &&
                   string.Equals(TypeMappings.ConvertSynonyms(RawType()), TypeMappings.ConvertSynonyms(other.RawType()));
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
                return (Name.GetHashCode()*397) ^ Type.GetHashCode();
            }
        }

        public string ToDeclaration(int length)
        {
            return $"{Name.PadRight(length)}{Type} {Directive}";
        }

        public override string ToString()
        {
            return $"{Name} {Type} {Directive}";
        }
    }
}
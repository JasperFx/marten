namespace Marten.Generation
{
    public class TableColumn
    {
        public string Name;
        public string Type;
        public string Directive;

        public TableColumn(string name, string type)
        {
            Name = name;
            Type = type;
        }

        protected bool Equals(TableColumn other)
        {
            return string.Equals(Name, other.Name) && string.Equals(Type, other.Type);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TableColumn) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0)*397) ^ (Type != null ? Type.GetHashCode() : 0);
            }
        }
    }
}
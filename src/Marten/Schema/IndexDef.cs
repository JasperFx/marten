namespace Marten.Schema
{
    public class IndexDef
    {
        public TableName Table { get; }

        public string Name { get; }
        public string DDL { get; }

        public IndexDef(TableName table, string name, string ddl)
        {
            Table = table;
            Name = name;
            DDL = ddl;
        }

        public override string ToString()
        {
            return $"Table: {Table}, Name: {Name}, DDL: {DDL}";
        }

        protected bool Equals(IndexDef other)
        {
            return string.Equals(Name, other.Name) && string.Equals(DDL, other.DDL);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((IndexDef) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0)*397) ^ (DDL != null ? DDL.GetHashCode() : 0);
            }
        }
    }
}
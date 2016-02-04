namespace Marten.Schema.Hierarchies
{
    public class HierarchyArgument : StorageArgument
    {
        public HierarchyMapping Mapping { get; set; }

        public HierarchyArgument(HierarchyMapping mapping) : base("hierarchy", typeof(HierarchyMapping))
        {
            Mapping = mapping;
        }

        public override object GetValue(IDocumentSchema schema)
        {
            return Mapping;
        }

        protected bool Equals(HierarchyArgument other)
        {
            return Equals(Mapping, other.Mapping);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((HierarchyArgument) obj);
        }

        public override int GetHashCode()
        {
            return (Mapping != null ? Mapping.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return $"Mapping: {Mapping}";
        }
    }
}
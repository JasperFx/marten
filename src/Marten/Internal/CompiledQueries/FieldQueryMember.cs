using System.Reflection;

namespace Marten.Internal.CompiledQueries
{
    public class FieldQueryMember<T>: QueryMember<T>
    {
        private readonly FieldInfo _field;

        public FieldQueryMember(FieldInfo field) : base(field)
        {
            _field = field;
        }

        public override T GetValue(object query)
        {
            return (T) _field.GetValue(query);
        }

        public override void SetValue(object query, T value)
        {
            _field.SetValue(query, value);
        }

        public override bool CanWrite()
        {
            return _field.IsPublic;
        }
    }
}

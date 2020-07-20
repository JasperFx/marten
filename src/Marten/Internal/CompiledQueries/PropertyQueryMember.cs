using System.Reflection;

namespace Marten.Internal.CompiledQueries
{
    public class PropertyQueryMember<T>: QueryMember<T>
    {
        private readonly PropertyInfo _property;

        public PropertyQueryMember(PropertyInfo property) : base(property)
        {
            _property = property;
        }

        public override bool CanWrite()
        {
            return _property.CanWrite;
        }

        public override T GetValue(object query)
        {
            return (T) _property.GetValue(query);
        }

        public override void SetValue(object query, T value)
        {
            _property.SetValue(query, value);
        }
    }
}

using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Filters
{
    internal class CollectionIsEmpty: ISqlFragment
    {
        private readonly ArrayField _field;

        public CollectionIsEmpty(ArrayField field)
        {
            _field = field;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("(");
            builder.Append(_field.RawLocator);
            builder.Append(" is null or jsonb_array_length(");
            builder.Append(_field.RawLocator);
            builder.Append(") = 0)");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}

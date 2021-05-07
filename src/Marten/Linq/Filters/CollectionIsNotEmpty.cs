using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.Filters
{
    internal class CollectionIsNotEmpty: IReversibleWhereFragment
    {
        private readonly ArrayField _field;

        public CollectionIsNotEmpty(ArrayField field)
        {
            _field = field;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("(");
            builder.Append(_field.RawLocator);
            builder.Append(" is not null and jsonb_array_length(");
            builder.Append(_field.RawLocator);
            builder.Append(") > 0)");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }

        public ISqlFragment Reverse()
        {
            return new CollectionIsEmpty(_field);
        }
    }
}

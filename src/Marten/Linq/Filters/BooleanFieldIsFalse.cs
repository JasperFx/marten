using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Filters
{
    public class BooleanFieldIsFalse: IReversibleWhereFragment
    {
        private readonly IField _field;

        public BooleanFieldIsFalse(IField field)
        {
            _field = field;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("(");
            builder.Append(_field.RawLocator);
            builder.Append(" is null or ");
            builder.Append(_field.TypedLocator);
            builder.Append(" = False)");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }

        public ISqlFragment Reverse()
        {
            return new BooleanFieldIsTrue(_field);
        }
    }
}

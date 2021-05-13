using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Filters
{
    public class BooleanFieldIsTrue: IReversibleWhereFragment
    {
        private readonly IField _field;

        public BooleanFieldIsTrue(IField field)
        {
            _field = field;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("(");
            builder.Append(_field.RawLocator);
            builder.Append(" is not null and ");
            builder.Append(_field.TypedLocator);
            builder.Append(" = True)");
        }

        public bool Contains(string sqlText)
        {
            return _field.RawLocator.Contains(sqlText);
        }

        public ISqlFragment Reverse()
        {
            return new BooleanFieldIsFalse(_field);
        }
    }
}

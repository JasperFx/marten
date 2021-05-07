using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.Filters
{
    public class IsNullFilter : IReversibleWhereFragment
    {
        public IsNullFilter(IField field)
        {
            Field = field;
        }

        public IField Field { get; }

        public void Apply(CommandBuilder builder)
        {
            builder.Append(Field.RawLocator);
            builder.Append(" is null");
        }

        public bool Contains(string sqlText)
        {
            return Field.Contains(sqlText);
        }

        public ISqlFragment Reverse()
        {
            return new IsNotNullFilter(Field);
        }
    }
}

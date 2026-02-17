using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    public class LowerCaseFragment: ISqlFragment
    {
        private readonly ISqlFragment _inner;

        public LowerCaseFragment(ISqlFragment inner)
        {
            _inner = inner;
        }

        public void Apply(ICommandBuilder builder)
        {
            builder.Append("LOWER(");
            _inner.Apply(builder);
            builder.Append(")");
        }
    }
}

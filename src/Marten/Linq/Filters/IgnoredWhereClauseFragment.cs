using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Filters
{
    internal class IgnoredWhereClauseFragment: ISqlFragment
    {
        public void Apply(CommandBuilder builder)
        {
        }
        public bool Contains(string sqlText) => false;
    }
}

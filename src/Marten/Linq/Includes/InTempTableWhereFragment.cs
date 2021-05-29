using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Includes
{
    internal class InTempTableWhereFragment: ISqlFragment
    {
        private readonly string _tempTableName;
        private readonly string _tempTableColumn;

        public InTempTableWhereFragment(string tempTableName, string tempTableColumn)
        {
            _tempTableName = tempTableName;
            _tempTableColumn = tempTableColumn;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("id in (select ");
            builder.Append(_tempTableColumn);
            builder.Append(" from ");
            builder.Append(_tempTableName);
            builder.Append(")");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}

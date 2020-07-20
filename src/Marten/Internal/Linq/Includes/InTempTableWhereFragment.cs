using Marten.Linq;
using Marten.Util;

namespace Marten.Internal.Linq.Includes
{
    public class InTempTableWhereFragment: IWhereFragment
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

using Marten.Schema;

namespace Marten.Linq.Filters
{
    public class ExcludeSoftDeletedFilter : WhereFragment
    {
        public static readonly ExcludeSoftDeletedFilter Instance
            = new ExcludeSoftDeletedFilter();

        public ExcludeSoftDeletedFilter() : base($"d.{SchemaConstants.DeletedColumn} = False")
        {
        }
    }
}

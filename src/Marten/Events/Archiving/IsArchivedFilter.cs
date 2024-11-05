using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Archiving;

internal class IsArchivedFilter: IArchiveFilter, IReversibleWhereFragment
{
    private static readonly string _sql = $"d.{IsArchivedColumn.ColumnName} = TRUE";

    public static readonly IsArchivedFilter Instance = new IsArchivedFilter();

    private IsArchivedFilter()
    {

    }

    public ISqlFragment Reverse()
    {
        return IsNotArchivedFilter.Instance;
    }

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append(_sql);
    }
}

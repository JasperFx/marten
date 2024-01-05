using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Archiving;

internal class IsNotArchivedFilter: IArchiveFilter, IReversibleWhereFragment
{
    private static readonly string _sql = $"d.{IsArchivedColumn.ColumnName} = FALSE";

    public static readonly IsNotArchivedFilter Instance = new();

    private IsNotArchivedFilter()
    {
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }

    public ISqlFragment Reverse()
    {
        return IsArchivedFilter.Instance;
    }
}

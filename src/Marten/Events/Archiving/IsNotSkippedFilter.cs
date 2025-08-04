using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Archiving;

internal class IsNotSkippedFilter: IReversibleWhereFragment
{
    private static readonly string _sql = $"d.is_skipped = FALSE";

    public static readonly IsNotSkippedFilter Instance = new();

    private IsNotSkippedFilter()
    {
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }

    public ISqlFragment Reverse()
    {
        return IsSkippedFilter.Instance;
    }
}
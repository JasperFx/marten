using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Archiving;

internal class IsSkippedFilter: IReversibleWhereFragment
{
    private static readonly string _sql = $"d.is_skipped = TRUE";

    public static readonly IsSkippedFilter Instance = new();

    private IsSkippedFilter()
    {
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }

    public ISqlFragment Reverse()
    {
        return IsNotSkippedFilter.Instance;
    }
}
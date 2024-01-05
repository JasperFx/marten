using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;


internal interface ISoftDeletedFilter: ISqlFragment
{

}

internal class ExcludeSoftDeletedFilter: ISoftDeletedFilter
{
    public static readonly ExcludeSoftDeletedFilter Instance = new();

    private static string _sql = $"d.{SchemaConstants.DeletedColumn} = False";

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }
}

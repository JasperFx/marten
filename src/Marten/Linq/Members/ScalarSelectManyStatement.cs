#nullable enable
using Marten.Linq.SqlGeneration;
using Weasel.Core.Serialization;
using Weasel.Postgresql;

namespace Marten.Linq.Members;

internal class ScalarSelectManyStatement<T>: SelectorStatement where T : struct
{
    public ScalarSelectManyStatement(SelectorStatement parent, ISerializer serializer)
    {
        SelectClause = new ScalarSelectClause<T>(ToLocator(serializer), parent.ExportName);
    }

    public static string ToLocator(ISerializer serializer)
    {
        return $"CAST(data as {PostgresqlProvider.Instance.GetDatabaseType(typeof(T), serializer.EnumStorage)})";
    }
}

using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration;

internal class ScalarSelectManyStatement<T>: SelectorStatement where T : struct
{
    public ScalarSelectManyStatement(SelectorStatement parent, ISerializer serializer): base(
        new ScalarSelectClause<T>(ToLocator(serializer), parent.ExportName), null)
    {
    }

    public static string ToLocator(ISerializer serializer)
    {
        return $"CAST(data as {PostgresqlProvider.Instance.GetDatabaseType(typeof(T), serializer.EnumStorage)})";
    }
}

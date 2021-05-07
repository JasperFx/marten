using Marten.Util;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration
{
    public class ScalarSelectManyStatement<T>: SelectorStatement where T : struct
    {
        public static string ToLocator(ISerializer serializer)
        {
            return $"CAST(data as {TypeMappings.GetPgType(typeof(T), serializer.EnumStorage)})";
        }

        public ScalarSelectManyStatement(SelectorStatement parent, ISerializer serializer) : base(new ScalarSelectClause<T>(ToLocator(serializer), parent.ExportName), null)
        {
        }
    }
}

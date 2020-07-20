using Marten.Util;

namespace Marten.Internal.Linq
{
    public class ScalarSelectManyStatement<T>: Statement where T : struct
    {
        public static string ToLocator(ISerializer serializer)
        {
            return $"CAST(data as {TypeMappings.GetPgType(typeof(T), serializer.EnumStorage)})";
        }

        public ScalarSelectManyStatement(Statement parent, ISerializer serializer) : base(new ScalarSelectClause<T>(ToLocator(serializer), parent.ExportName), null)
        {
        }
    }
}

namespace Marten.Storage
{
    internal static class OriginWriter
    {
        private static readonly string MartenFqn = typeof(IDocumentStore).AssemblyQualifiedName;

        public static string OriginStatement(string objectType, string objectName)
        {
            return $"COMMENT ON {objectType} {objectName} IS 'origin:{MartenFqn}';";
        }
    }
}

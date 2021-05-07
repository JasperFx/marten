using System;
using Weasel.Postgresql.Tables;

namespace Marten.Storage
{
    [Obsolete("Move this to Weasel")]
    internal static class OriginWriter
    {
        private static readonly string MartenFqn = typeof(IDocumentStore).AssemblyQualifiedName;

        public static string OriginStatement(string objectType, string objectName)
        {
            return $"COMMENT ON {objectType} {objectName} IS 'origin:{MartenFqn}';";
        }

        public static string OriginStatement(this Table definition)
        {
            return OriginStatement("TABLE", definition.Identifier.QualifiedName);
        }
    }
}

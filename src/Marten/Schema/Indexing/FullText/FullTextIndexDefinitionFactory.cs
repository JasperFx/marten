using System.Linq;
using System.Reflection;
using JasperFx.Core;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Indexes;

#nullable enable

namespace Marten.Schema.Indexing.FullText;

public static class FullTextIndexDefinitionFactory
{
    public static FullTextIndexDefinition From(
        DocumentMapping mapping,
        string? regConfig = null,
        string? dataConfig = null,
        string? indexName = null
    ) =>
        new(
            PostgresqlObjectName.From(mapping.TableName),
            dataConfig ?? FullTextIndexDefinition.DataDocumentConfig,
            regConfig ?? FullTextIndexDefinition.DefaultRegConfig,
            indexName,
            SchemaConstants.MartenPrefix
        );

    public static FullTextIndexDefinition From(
        DocumentMapping mapping,
        MemberInfo[][] members,
        string? regConfig = null
    ) =>
        new(
            PostgresqlObjectName.From(mapping.TableName),
            GetDataConfig(mapping, members),
            regConfig ?? FullTextIndexDefinition.DefaultRegConfig,
            indexPrefix: SchemaConstants.MartenPrefix
        );

    private static string GetDataConfig(DocumentMapping mapping, MemberInfo[][] members)
    {
        var dataConfig = members
            .Select(m => $"({mapping.QueryMembers.MemberFor(m).RawLocator.Replace("d.", "")})")
            .Join(" || ' ' || ");

        return $"({dataConfig})";
    }
}

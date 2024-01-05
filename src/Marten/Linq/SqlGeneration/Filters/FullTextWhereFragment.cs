using System.Linq;
using Marten.Linq.Parsing.Methods.FullText;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Linq.SqlGeneration.Filters;

internal class FullTextWhereFragment: ISqlFragment
{
    private readonly string _dataConfig;
    private readonly string _regConfig;
    private readonly FullTextSearchFunction _searchFunction;
    private readonly string _searchTerm;

    public FullTextWhereFragment(DocumentMapping mapping, FullTextSearchFunction searchFunction, string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig)
    {
        _regConfig = regConfig;

        _dataConfig = GetDataConfig(mapping, regConfig).Replace("data", "d.data");
        _searchFunction = searchFunction;
        _searchTerm = searchTerm;
    }

    // don't parameterize full-text search config as it ruins the performance with the query plan in PG
    private string Sql =>
        $"to_tsvector('{_regConfig}'::regconfig, {_dataConfig}) @@ {_searchFunction}('{_regConfig}'::regconfig, ?)";

    public void Apply(ICommandBuilder builder)
    {
        builder.AppendWithParameters(Sql)[0].Value = _searchTerm;
    }

    private static string GetDataConfig(DocumentMapping mapping, string regConfig)
    {
        if (mapping == null)
        {
            return FullTextIndexDefinition.DataDocumentConfig;
        }

        return mapping
            .Indexes
            .OfType<FullTextIndexDefinition>()
            .Where(i => i.RegConfig == regConfig)
            .Select(i => i.DataConfig)
            .FirstOrDefault() ?? FullTextIndexDefinition.DataDocumentConfig;
    }
}

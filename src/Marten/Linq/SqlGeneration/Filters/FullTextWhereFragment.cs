#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Marten.Linq.Parsing.Methods.FullText;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Linq.SqlGeneration.Filters;

internal class FullTextWhereFragment: ISqlFragment
{
    // PostgreSQL text-search configuration names are stored as identifiers in
    // pg_ts_config (see https://www.postgresql.org/docs/current/textsearch-configuration.html).
    // We allow simple unquoted identifiers — optionally schema-qualified — so values
    // like "english", "french", or "pg_catalog.english" pass through, while anything
    // containing whitespace, quotes, semicolons, or other punctuation is rejected.
    // This is a security-critical check: regConfig is interpolated into SQL by Sql below,
    // so any value that escapes this pattern would be a SQL injection sink.
    private static readonly Regex _regConfigPattern = new(
        @"^[a-zA-Z_][a-zA-Z0-9_]{0,62}(\.[a-zA-Z_][a-zA-Z0-9_]{0,62})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _dataConfig;
    private readonly string _regConfig;
    private readonly FullTextSearchFunction _searchFunction;
    private readonly string _searchTerm;

    public FullTextWhereFragment(DocumentMapping? mapping, FullTextSearchFunction searchFunction, string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig)
    {
        ValidateRegConfig(regConfig);

        _regConfig = regConfig;

        _dataConfig = GetDataConfig(mapping, regConfig).Replace("data", "d.data");
        _searchFunction = searchFunction;
        _searchTerm = searchTerm;
    }

    private static void ValidateRegConfig(string regConfig)
    {
        if (regConfig is null)
        {
            throw new ArgumentNullException(nameof(regConfig));
        }

        if (!_regConfigPattern.IsMatch(regConfig))
        {
            throw new ArgumentException(
                $"Invalid PostgreSQL text-search configuration name '{regConfig}'. " +
                "regConfig must be a simple PostgreSQL identifier (optionally schema-qualified), " +
                "matching ^[a-zA-Z_][a-zA-Z0-9_]*(\\.[a-zA-Z_][a-zA-Z0-9_]*)?$.",
                nameof(regConfig));
        }
    }

    // don't parameterize full-text search config as it ruins the performance with the query plan in PG
    private string Sql =>
        $"to_tsvector('{_regConfig}'::regconfig, {_dataConfig}) @@ {_searchFunction}('{_regConfig}'::regconfig, ?)";

    public void Apply(ICommandBuilder builder)
    {
        builder.AppendWithParameters(Sql)[0].Value = _searchTerm;
    }

    private static string GetDataConfig(DocumentMapping? mapping, string regConfig)
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

using System;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Schema;

/// <summary>
///     Implements an index that extracts ngrams for the specified field.
/// </summary>
public class NgramIndex: IndexDefinition
{
    public const string DefaultRegConfig = "english";
    public const string DefaultDataConfig = "data";

    // Hold a reference to the DocumentMapping (not a snapshot of TableName or
    // DatabaseSchemaName) so the index name we derive in deriveIndexName()
    // tracks any post-construction mutation of the mapping's alias — most
    // notably the `_{Version}` suffix that ProjectionVersionAliasPolicy
    // appends to documents owned by a versioned aggregate projection (#4367).
    // DocumentIndex / ComputedIndex follow the same lazy pattern; NgramIndex
    // was the outlier capturing TableName at construction time.
    private readonly DocumentMapping _parent;

    private string _dataConfig = null!;
    private readonly string? _indexName;

    public NgramIndex(DocumentMapping mapping, string? dataConfig = null, string? indexName = null)
    {
        _parent = mapping;
        DataConfig = dataConfig;
        _indexName = indexName;

        Method = IndexMethod.gin;
    }

    public NgramIndex(DocumentMapping mapping, MemberInfo[] member)
        : this(mapping, GetDataConfig(mapping, member))
    {
    }

    /// <summary>
    ///     Gets or sets the data config.
    /// </summary>
    public string? DataConfig
    {
        get => _dataConfig;
        set => _dataConfig = value ?? DefaultDataConfig;
    }

    public override string[] Columns => [$"{_parent.DatabaseSchemaName}.mt_grams_vector( {_dataConfig})"];
    protected override string deriveIndexName()
    {
        var lowerValue = _indexName?.ToLowerInvariant();
        if (lowerValue?.StartsWith(SchemaConstants.MartenPrefix) == true)
        {
            return lowerValue.ToLowerInvariant();
        }

        if (lowerValue?.IsNotEmpty() == true)
        {
            return SchemaConstants.MartenPrefix + lowerValue.ToLowerInvariant();
        }

        var indexFieldName = _dataConfig.ToLowerInvariant();
        indexFieldName = indexFieldName.Split([" as "], StringSplitOptions.None)[0];
        indexFieldName = indexFieldName
            .Replace("cast(", string.Empty)
            .Replace("data", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("'", string.Empty)
            .Replace("->>", string.Empty)
            .Replace("->", string.Empty);

        return $"{_parent.TableName.Name}_idx_ngram_{indexFieldName}";
    }

    private static string GetDataConfig(DocumentMapping mapping, MemberInfo[] members)
    {
        var dataConfig = $"{mapping.QueryMembers.MemberFor(members).TypedLocator.RemoveTableAlias("d")}";

        return $"{dataConfig}";
    }
}

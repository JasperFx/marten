using System;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using Marten.Linq;
using Marten.Linq.Parsing;
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
    private readonly DbObjectName _table;

    private string _dataConfig;
    private readonly string _indexName;

    public NgramIndex(DocumentMapping mapping, string dataConfig = null, string indexName = null)
    {
        _table = mapping.TableName;
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
    public string DataConfig
    {
        get => _dataConfig;
        set => _dataConfig = value ?? DefaultDataConfig;
    }

    public override string[] Columns
    {
        get => new[] { $"mt_grams_vector( {_dataConfig})" };
        set
        {
            // nothing
        }
    }

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

        var arrowIndex = _dataConfig.IndexOf("->>", StringComparison.InvariantCultureIgnoreCase);

        var indexFieldName = arrowIndex != -1
            ? _dataConfig.Substring(arrowIndex + 3).Trim().Replace("'", string.Empty).ToLowerInvariant()
            : _dataConfig;
        return $"{_table.Name}_idx_ngram_{indexFieldName}";
    }

    private static string GetDataConfig(DocumentMapping mapping, MemberInfo[] members)
    {
        var dataConfig = members
            .Select(m => $"{mapping.QueryMembers.MemberFor(members).TypedLocator.Replace("d.", "")}")
            .Join(" || ' ' || ");

        return $"{dataConfig}";
    }
}

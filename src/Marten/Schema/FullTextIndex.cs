using System.Linq;
using System.Reflection;
using JasperFx.Core;
using Marten.Linq;
using Marten.Linq.Parsing;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Schema;

public class FullTextIndex: IndexDefinition
{
    public const string DefaultRegConfig = "english";
    public const string DefaultDataConfig = "data";
    private readonly DbObjectName _table;
    private string _dataConfig;
    private readonly string _indexName;

    private string _regConfig;

    public FullTextIndex(DocumentMapping mapping, string regConfig = null, string dataConfig = null,
        string indexName = null)
    {
        _table = mapping.TableName;
        RegConfig = regConfig;
        DataConfig = dataConfig;
        _indexName = indexName;

        Method = IndexMethod.gin;
    }

    public FullTextIndex(DocumentMapping mapping, string regConfig, MemberInfo[][] members)
        : this(mapping, regConfig, GetDataConfig(mapping, members))
    {
    }


    public string RegConfig
    {
        get => _regConfig;
        set => _regConfig = value ?? DefaultRegConfig;
    }

    public string DataConfig
    {
        get => _dataConfig;
        set => _dataConfig = value ?? DefaultDataConfig;
    }

    public override string[] Columns
    {
        get => new[] { $"to_tsvector('{_regConfig}',{_dataConfig.Trim()})" };
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

        if (_regConfig != DefaultRegConfig)
        {
            return $"{_table.Name}_{_regConfig}_idx_fts";
        }

        return $"{_table.Name}_idx_fts";
    }

    private static string GetDataConfig(DocumentMapping mapping, MemberInfo[][] members)
    {
        var dataConfig = members
            .Select(m => $"({mapping.QueryMembers.MemberFor(m).RawLocator.Replace("d.", "")})")
            .Join(" || ' ' || ");

        return $"({dataConfig})";
    }
}

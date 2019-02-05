using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Storage;

namespace Marten.Schema
{
    public class FullTextIndex : IIndexDefinition
    {
        public const string DefaultRegConfig = "english";
        public const string DefaultDataConfig = "data";

        private string _regConfig;
        private string _dataConfig;
        private readonly DbObjectName _table;
        private string _indexName;

        public FullTextIndex(DocumentMapping mapping, string regConfig = null, string dataConfig = null, string indexName = null)
        {
            _table = mapping.Table;
            RegConfig = regConfig;
            DataConfig = dataConfig;
            IndexName = indexName;
        }

        public FullTextIndex(DocumentMapping mapping, string regConfig, MemberInfo[][] members)
            : this(mapping, regConfig, GetDataConfig(mapping, members))
        {
        }

        public string IndexName
        {
            get
            {
                var lowerValue = _indexName?.ToLowerInvariant();
                if (lowerValue?.StartsWith(DocumentMapping.MartenPrefix) == true)
                    return lowerValue.ToLowerInvariant();
                else if (lowerValue?.IsNotEmpty() == true)
                    return DocumentMapping.MartenPrefix + lowerValue.ToLowerInvariant();
                else if (_regConfig != DefaultRegConfig)
                    return $"{_table.Name}_{_regConfig}_idx_fts";
                else
                    return $"{_table.Name}_idx_fts";
            }
            set => _indexName = value;
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

        public string ToDDL()
        {
            return $"CREATE INDEX {IndexName} ON {_table.QualifiedName} USING gin (( to_tsvector('{_regConfig}', {_dataConfig}) ));";
        }

        public bool Matches(ActualIndex index)
        {
            var ddl = index?.DDL.ToLowerInvariant();
            // Check for the existence of the 'to_tsvector' function, the correct table name, and the use of the data column
            return ddl?.Contains("to_tsvector") == true
                && ddl?.Contains(IndexName) == true
                && ddl?.Contains(_table.QualifiedName) == true
                && ddl?.Contains(_regConfig) == true
                && ddl?.Contains(_dataConfig) == true;
        }

        private static string GetDataConfig(DocumentMapping mapping, MemberInfo[][] members)
        {
            var dataConfig = members
                    .Select(m => $"({mapping.FieldFor(m).SqlLocator.Replace("d.", "")})")
                    .Join(" || ' ' || ");

            return $"({dataConfig})";
        }

        private void RefreshIndexName()
        {
            IndexName = _indexName;
        }
    }
}
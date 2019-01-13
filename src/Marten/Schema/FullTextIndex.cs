using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Storage;

namespace Marten.Schema
{
    public class FullTextIndex : IIndexDefinition
    {
        public const string DefaultRegConfig = "english";
        private const string DefaultDataConfig = "data";

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
            get => _indexName;
            set
            {
                var lowerValue = value?.ToLowerInvariant();
                if (lowerValue?.StartsWith(DocumentMapping.MartenPrefix) == true)
                    _indexName = lowerValue.ToLowerInvariant();
                else if (lowerValue?.IsNotEmpty() == true)
                    _indexName = DocumentMapping.MartenPrefix + lowerValue.ToLowerInvariant();
                else if (_dataConfig != DefaultDataConfig)
                    _indexName = $"{_table.Name}_{_regConfig}_idx_fts";
                else
                    _indexName = $"{_table.Name}_idx_fts";
            }
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
                && ddl.Contains(IndexName)
                && ddl.Contains(_table.QualifiedName)
                && ddl.Contains(_regConfig)
                && ddl.Contains(_dataConfig);
        }

        private static string GetDataConfig(DocumentMapping mapping, MemberInfo[][] members)
        {
            var dataConfig = members
                    .Select(m => $"({mapping.FieldFor(m).SqlLocator.Replace("d.", "")})")
                    .Join(", ");

            return $"CONCAT({dataConfig})";
        }
    }
}
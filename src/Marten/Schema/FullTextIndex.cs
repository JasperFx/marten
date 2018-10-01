using System;
using Baseline;
using Marten.Storage;

namespace Marten.Schema
{
    public class FullTextIndex : IIndexDefinition
    {
        private readonly string _config;
        private readonly DbObjectName _table;
        private string _indexName;

        public FullTextIndex(DocumentMapping parent, string config)
        {
            _table = parent.Table;
            _config = config;
            _indexName = $"{_table.Name}_idx_fts";
        }

        public string IndexName
        {
            get => _indexName;
            set
            {
                var lowerValue = value.ToLowerInvariant();
                if(value.IsNotEmpty() && lowerValue.StartsWith(DocumentMapping.MartenPrefix))
                    _indexName = lowerValue.ToLowerInvariant();
                else if(lowerValue.IsNotEmpty())
                    _indexName = DocumentMapping.MartenPrefix + lowerValue.ToLowerInvariant();
                else
                    _indexName = $"{_table.Name}_idx_fts";
            }
        }        
        public string ToDDL()
        {
            return $"CREATE INDEX {IndexName} ON {_table.QualifiedName} USING gin (( to_tsvector('{_config}', data) ));";
        }

        public bool Matches(ActualIndex index)
        {
            var ddl = index?.DDL.ToLowerInvariant();
            // Check for the existence of the 'to_tsvector' function, the correct table name, and the use of the data column
            return ddl?.Contains("to_tsvector") == true && ddl.Contains(_table.QualifiedName) && ddl.Contains("data");
        }
    }
}
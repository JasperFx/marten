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
        }

        public string IndexName
        {
            get
            {
                if(_indexName.IsNotEmpty())
                {
                    return _indexName.StartsWith(DocumentMapping.MartenPrefix)
                        ? _indexName
                        : DocumentMapping.MartenPrefix + _indexName;
                }

                return $"mt_{_table.Name}_idx_fts";
            }
            set => _indexName = value;
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
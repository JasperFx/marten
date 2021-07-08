using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Schema
{
    public class FullTextIndex: IndexDefinition
    {
        public const string DefaultRegConfig = "english";
        public const string DefaultDataConfig = "data";

        private string _regConfig;
        private string _dataConfig;
        private readonly DbObjectName _table;
        private string _indexName;

        public FullTextIndex(DocumentMapping mapping, string regConfig = null, string dataConfig = null, string indexName = null)
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

        protected override string deriveIndexName()
        {
            var lowerValue = _indexName?.ToLowerInvariant();
            if (lowerValue?.StartsWith(SchemaConstants.MartenPrefix) == true)
                return lowerValue.ToLowerInvariant();
            else if (lowerValue?.IsNotEmpty() == true)
                return SchemaConstants.MartenPrefix + lowerValue.ToLowerInvariant();
            else if (_regConfig != DefaultRegConfig)
                return $"{_table.Name}_{_regConfig}_idx_fts";
            else
                return $"{_table.Name}_idx_fts";
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
            get
            {
                return new string[] { $"( to_tsvector('{_regConfig}', {_dataConfig}) )"};
            }
            set
            {
                // nothing
            }
        }

        private static string GetDataConfig(DocumentMapping mapping, MemberInfo[][] members)
        {
            var dataConfig = members
                    .Select(m => $"({mapping.FieldFor(m).TypedLocator.Replace("d.", "")})")
                    .Join(" || ' ' || ");

            return $"({dataConfig})";
        }
    }
}

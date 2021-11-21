using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Schema
{
    /// <summary>
    /// Implements an index that extracts ngrams for the specified field.
    /// </summary>
    public class NgramIndex: IndexDefinition
    {
        public const string DefaultRegConfig = "english";
        public const string DefaultDataConfig = "data";

        private string _dataConfig;
        private readonly DbObjectName _table;
        private string _indexName;

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

        protected override string deriveIndexName()
        {
            var lowerValue = _indexName?.ToLowerInvariant();
            if (lowerValue?.StartsWith(SchemaConstants.MartenPrefix) == true)
                return lowerValue.ToLowerInvariant();
            else if (lowerValue?.IsNotEmpty() == true)
                return SchemaConstants.MartenPrefix + lowerValue.ToLowerInvariant();
            else
            {
                var arrowIndex = _dataConfig.IndexOf("->>", StringComparison.InvariantCultureIgnoreCase);

                var indexFieldName = arrowIndex != -1 ? _dataConfig.Substring(arrowIndex + 3).Trim().Replace("'", string.Empty).ToLowerInvariant() : _dataConfig;
                return $"{_table.Name}_idx_ngram_{indexFieldName}";
            }
        }

        /// <summary>
        /// Gets or sets the data config.
        /// </summary>
        public string DataConfig
        {
            get => _dataConfig;
            set => _dataConfig = value ?? DefaultDataConfig;
        }

        public override string[] Columns
        {
            get
            {
                return new string[] { $"mt_grams_vector( {_dataConfig})" };
            }
            set
            {
                // nothing
            }
        }

        private static string GetDataConfig(DocumentMapping mapping, MemberInfo[] members)
        {
            var dataConfig = members
                .Select(m => $"{mapping.FieldFor(m).TypedLocator.Replace("d.", "")}")
                .Join(" || ' ' || ");

            return $"{dataConfig}";
        }
    }
}

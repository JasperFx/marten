using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Marten.Storage;
using Baseline;

namespace Marten.Schema
{
    public class IndexDefinition : IIndexDefinition
    {
        private readonly DocumentMapping _parent;
        private readonly string[] _columns;
        private string _indexName;

        public IndexDefinition(DocumentMapping parent, params string[] columns)
        {
            _parent = parent;
            _columns = columns;
        }

        public IndexMethod Method { get; set; } = IndexMethod.btree;

        public bool IsUnique { get; set; }

        public bool IsConcurrent { get; set; }

        public string IndexName
        {
            get
            {
                if (_indexName.IsNotEmpty())
                {
                    return _indexName.StartsWith(DocumentMapping.MartenPrefix)
                        ? _indexName.ToLowerInvariant()
                        : DocumentMapping.MartenPrefix + _indexName.ToLowerInvariant();
                }
                return $"{_parent.Table.Name}_idx_{_columns.Join("_")}";
            }
            set { _indexName = value; }
        }

        public string Expression { get; set; }

        public string Modifier { get; set; }

        public IEnumerable<string> Columns => _columns;

        public string ToDDL()
        {
            var index = IsUnique ? "CREATE UNIQUE INDEX" : "CREATE INDEX";
            if (IsConcurrent)
            {
                index += " CONCURRENTLY";
            }

            index += $" {IndexName} ON {_parent.Table.QualifiedName}";

            if (Method != IndexMethod.btree)
            {
                index += $" USING {Method}";
            }

            var columns = _columns.Select(column => $"\"{column}\"").Join(", ");
            if (Expression.IsEmpty())
            {
                index += $" ({columns})";
            }
            else
            {
                index += $" ({Expression.Replace("?", columns)})";
            }

            if (Modifier.IsNotEmpty())
            {
                index += " " + Modifier;
            }

            return index + ";";
        }

        public bool Matches(ActualIndex index)
        {
            if (!index.Name.EqualsIgnoreCase(IndexName)) return false;

            var actual = index.DDL;
            if (Method == IndexMethod.btree)
            {
                actual = actual.Replace("USING btree", "");
            }

            var columnsGroupPattern = "(?<columns>.*(?:(?:[\\w.]+)\\s?(?:[\\w_]+).*))";
            var columnsMatchPattern = $"\\({columnsGroupPattern}\\)";

            if (Expression.IsNotEmpty())
            {
                var escapedExpression = Regex.Escape(Expression);

                columnsMatchPattern = $"\\({escapedExpression.Replace("\\?", columnsGroupPattern)}\\)";
            }

            var match = Regex.Match(actual, columnsMatchPattern);

            if (match.Success)
            {
                var columns = match.Groups["columns"].Value;
                _columns.Each(col =>
                {
                    columns = Regex.Replace(columns, $"({col})\\s?([\\w_]+)?", "\"$1\"$2");
                });

                var replacement = Expression.IsEmpty() ?
                    $"({columns.Trim()})" :
                    $"({Expression.Replace("?", columns.Trim())})";

                actual = Regex.Replace(actual, columnsMatchPattern, replacement);
            }

            if (!actual.Contains(_parent.Table.QualifiedName))
            {
                actual = actual.Replace("ON " + _parent.Table.Name, "ON " + _parent.Table.QualifiedName);
            }

            actual = actual.Replace("  ", " ") + ";";

            // if column name being a PostgreSQL keyword, column is already wrapped with double quotes
            // above regex and replace logic will result in additional double quotes, remove the same
            actual = actual.Replace("\"\"", "\"");

            return ToDDL().EqualsIgnoreCase(actual);
        }
    }

    public enum IndexMethod
    {
        btree,
        hash,
        gist,
        gin,
        brin
    }
}

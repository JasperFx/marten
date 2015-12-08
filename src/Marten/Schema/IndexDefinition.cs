using System.Collections.Generic;
using Baseline;

namespace Marten.Schema
{
    public class IndexDefinition
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
                return _indexName ?? $"{_parent.TableName}_idx_{_columns.Join("_")}";
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

            index += $" {IndexName} ON {_parent.TableName}";

            if (Method != IndexMethod.btree)
            {
                index += $" USING {Method}";
            }


            var columns = _columns.Join(", ");
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

            return index;
        }
    }

    public enum IndexMethod
    {
        btree,
        hash,
        gist,
        gin
    }
}
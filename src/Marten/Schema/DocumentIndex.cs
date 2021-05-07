using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Baseline;
using Marten.Schema.Indexing.Unique;
using Marten.Storage.Metadata;
using Weasel.Postgresql.Tables;

namespace Marten.Schema
{
    public class DocumentIndex : IndexDefinition
    {
        private readonly DocumentMapping _parent;
        private readonly string[] _columns;

        public DocumentIndex(DocumentMapping parent, params string[] columns)
        {
            _parent = parent;
            _columns = columns;
        }

        public override string ToString()
        {
            return $"DocumentIndex for {_parent.DocumentType} on columns {Columns.Join(", ")}";
        }

        public TenancyScope TenancyScope { get; set; } = TenancyScope.Global;

        public override string[] Columns
        {
            get
            {
                if (TenancyScope == TenancyScope.Global)
                {
                    return _columns;
                }

                return _columns.Concat(new[] {TenantIdColumn.Name}).ToArray();
            }
            set
            {

            }
        }

        protected override string deriveIndexName()
        {
            return $"{_parent.TableName.Name}_idx_{_columns.Join("_")}";
        }

    }

}

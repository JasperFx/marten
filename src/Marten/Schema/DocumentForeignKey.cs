using System;
using System.Text;
using Baseline;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Schema
{
    public class DocumentForeignKey : ForeignKey
    {
        private static string toKeyName(DocumentMapping parent, DocumentMapping reference, string columnName)
        {
            return $"{parent.TableName.Name}_{columnName}{(parent.TenancyStyle == TenancyStyle.Conjoined && reference?.TenancyStyle == TenancyStyle.Conjoined ? "_tenant_id" : "")}_fkey";
        }

        public DocumentForeignKey(
            string columnName,
            DocumentMapping parent,
            DocumentMapping reference
        ) : base(toKeyName(parent, reference, columnName))
        {
            ReferenceDocumentType = reference.DocumentType;

            LinkedTable = reference.TableName;

            if (parent.TenancyStyle == TenancyStyle.Conjoined && reference?.TenancyStyle == TenancyStyle.Conjoined)
            {
                ColumnNames = new[] {columnName, TenantIdColumn.Name};
                LinkedNames = new[] {"id", TenantIdColumn.Name};
            }
            else
            {
                ColumnNames = new[] {columnName};
                LinkedNames = new[] {"id"};
            }
        }

        public Type ReferenceDocumentType { get; }
    }



}

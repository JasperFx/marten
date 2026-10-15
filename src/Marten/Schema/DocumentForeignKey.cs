using System;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Schema;

public class DocumentForeignKey: ForeignKey
{
    private readonly string _generatedName;

    public DocumentForeignKey(
        string columnName,
        DocumentMapping parent,
        DocumentMapping reference
    ): base(toKeyName(parent, reference, columnName))
    {
        // #5672. Read back rather than recomputed, because the base constructor unquotes the name it was
        // given. Anything else in Name later is the caller's, set through the foreignKeyConfiguration
        // callback, and must survive the tenant-id reordering in ForeignKeyExtensions.TryMoveTenantIdFirst.
        _generatedName = Name;

        ReferenceDocumentType = reference.DocumentType;

        LinkedTable = reference.TableName;

        if (parent.TenancyStyle == TenancyStyle.Conjoined && reference?.TenancyStyle == TenancyStyle.Conjoined)
        {
            ColumnNames = new[] { columnName, TenantIdColumn.Name };
            LinkedNames = new[] { "id", TenantIdColumn.Name };
        }
        else
        {
            ColumnNames = new[] { columnName };
            LinkedNames = new[] { "id" };
        }
    }

    public Type ReferenceDocumentType { get; }

    /// <summary>
    ///     Whether <c>Name</c> is still the one this constructor generated, as opposed to one the
    ///     <c>foreignKeyConfiguration</c> callback set. #5672: only a generated name may be regenerated when
    ///     tenant_id moves to the front of the key.
    /// </summary>
    internal bool HasGeneratedName => Name == _generatedName;

    /// <remarks>
    ///     Shortened for the same reason the document table's primary key name is (#5271/#4679, see
    ///     <c>DocumentTable</c>): a document type of no great length runs past PostgreSQL's 63 character
    ///     limit once <c>tenant_id</c> joins the key, and Marten then refuses to start. <c>Shorten</c> is a
    ///     no-op at 63 characters or fewer, so nothing changes for a name that already fit, and it appends a
    ///     deterministic hash rather than truncating, so two long type names that agree for the first 58
    ///     characters do not collide.
    /// </remarks>
    private static string toKeyName(DocumentMapping parent, DocumentMapping reference, string columnName)
    {
        return PostgresqlIdentifier.Shorten(
            $"{parent.TableName.Name}_{columnName}{(parent.TenancyStyle == TenancyStyle.Conjoined && reference?.TenancyStyle == TenancyStyle.Conjoined ? "_tenant_id" : "")}_fkey");
    }
}

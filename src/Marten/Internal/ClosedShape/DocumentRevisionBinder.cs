#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M8): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_version</c> column when the mapping uses
/// <see cref="ConcurrencyMode.Numeric"/>. The column is bigint rather
/// than uuid; the binder projects the loaded revision onto the
/// document's <c>[Version]</c>-annotated long member when one exists.
/// </summary>
/// <remarks>
/// Operations bind this slot themselves with the appropriate value
/// (caller-supplied <c>Revision</c> or auto-increment placeholder),
/// so <see cref="BindParameter"/> is only used when the operation
/// doesn't recognize the binder as the version slot — a defensive
/// default that writes <c>0</c> and lets the SQL's <c>CASE</c>
/// expression compute the effective value.
/// </remarks>
internal sealed class DocumentRevisionBinder<TDoc>: IRevisionMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, long>? _setter;
    private readonly StorageColumnType _columnType;

    public DocumentRevisionBinder(MemberInfo? revisionMember)
        : this(revisionMember, StorageColumnType.Long)
    {
    }

    public DocumentRevisionBinder(MemberInfo? revisionMember, StorageColumnType columnType)
    {
        // #4614: the mt_version column comes in two widths — bigint (default, used for
        // ILongVersioned docs) or integer (used for IRevisioned docs, restored Marten 8
        // behavior). The parameter NpgsqlDbType has to match the column type so Postgres
        // doesn't refuse the bind on the strict VALUES (CASE … END) path.
        _columnType = columnType;

        if (revisionMember is not null)
        {
            // #4526/#4528: regardless of column width, the document member is either int
            // (IRevisioned) or long (ILongVersioned). Read-side conversion (long → int)
            // handles the rare overflow path; the integer column can't overflow into an
            // int member, but a bigint column with an int member can. Those docs should
            // use ILongVersioned (documented caveat).
            if (revisionMember.GetRawMemberType() == typeof(int))
            {
                var intSetter = LambdaBuilder.Setter<TDoc, int>(revisionMember);
                _setter = (doc, revision) => intSetter(doc, (int)revision);
            }
            else
            {
                _setter = LambdaBuilder.Setter<TDoc, long>(revisionMember);
            }
        }
    }

    public StorageColumnType RevisionColumnType => _columnType;

    public string ColumnName => Marten.Schema.SchemaConstants.VersionColumn;

    public string ValueSql => "?";

    public void BindParameter(DbParameter parameter, TDoc document, IStorageSession session)
    {
        if (_columnType == StorageColumnType.Int)
        {
            parameter.Value = 0;
            ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlDbType.Integer;
        }
        else
        {
            parameter.Value = 0L;
            ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlDbType.Bigint;
        }
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        // Npgsql widens an int column to long via the field-value type handler, so
        // reading as long works for both column widths.
        var revision = reader.GetFieldValue<long>(columnOrdinal);
        _setter(document, revision);
    }

    /// <summary>
    /// W3 spike (M8): write a long revision directly onto the document's
    /// <c>[Version]</c>-annotated member.
    /// </summary>
    public void ApplyRevisionTo(TDoc document, long revision)
        => _setter?.Invoke(document, revision);

    public BulkColumnValue GetBulkValue(TDoc document)
    {
        // Bulk path defaults to revision 1 — matches the codegen
        // BulkLoader.GenerateBulkWriterCodeAsync's hard-coded "write
        // (long)1" for RevisionArgument. The parameter type follows the column.
        _setter?.Invoke(document, 1L);
        return _columnType == StorageColumnType.Int
            ? new BulkColumnValue(1, StorageColumnType.Int)
            : new BulkColumnValue(1L, StorageColumnType.Long);
    }
}

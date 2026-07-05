#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Abstract base for the per-<see cref="ConcurrencyMode"/> closed-shape
/// Update operation. Sealed subclasses provide the concrete
/// <see cref="ConfigureCommand"/> + <see cref="PostprocessAsync"/> bodies
/// so the hot path doesn't branch on <c>ConcurrencyMode</c> (#4659).
/// </summary>
/// <remarks>
/// Subclasses:
/// <list type="bullet">
/// <item><see cref="UnversionedClosedShapeUpdateOperation{TDoc, TId}"/> —
///       missing row → <see cref="Marten.Exceptions.NonExistentDocumentException"/>.</item>
/// <item><see cref="OptimisticClosedShapeUpdateOperation{TDoc, TId}"/> —
///       missing row → <see cref="Marten.Exceptions.ConcurrencyException"/>
///       (unless <see cref="IRevisionedOperation.IgnoreConcurrencyViolation"/>).</item>
/// <item><see cref="NumericClosedShapeUpdateOperation{TDoc, TId}"/> —
///       same as Optimistic but with a revision CASE/WHERE block.</item>
/// </list>
/// </remarks>
internal abstract class ClosedShapeUpdateOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation, IIdentifiedOperation<TDoc, TId>, JasperFx.Core.Exceptions.IExceptionTransform
    where TDoc : notnull
    where TId : notnull
{
    protected readonly TDoc _document;
    protected readonly TId _id;
    protected readonly string _tenantId;
    protected readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    protected ClosedShapeUpdateOperation(
        TDoc document,
        TId id,
        string tenantId,
        DocumentStorageDescriptor<TDoc, TId> descriptor)
    {
        _document = document;
        _id = id;
        _tenantId = tenantId;
        _descriptor = descriptor;
    }

    public TId Id => _id;

    public long Revision { get; set; }

    public bool IgnoreConcurrencyViolation { get; set; }

    public Type DocumentType => typeof(TDoc);

    public object Document => _document;

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IStorageSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Update;

    public abstract void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    public abstract Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);

    /// <summary>
    /// Bind data + client-side binders + id [+ tenant_id] [+ partition PK
    /// binders], stopping at the trailing concurrency WHERE slot. Returns
    /// the slot index for the concurrency-guard parameters that each
    /// concurrency-specific leaf appends.
    /// </summary>
    /// <remarks>
    /// Bug #4223: partitioned tables include the partition column(s) in
    /// the PK, so the WHERE clause adds a <c>{col} = ?</c> slot per
    /// partition column. Without this we'd update every row matching
    /// <c>id = ?</c> across partitions.
    /// </remarks>
    protected int BindPreConcurrencyParameters(NpgsqlParameter[] parameters, IStorageSession session)
    {
        foreach (var binder in _descriptor.WriteBinders)
        {
            binder.ApplyToDocument(_document, session);
        }

        session.Serializer.WriteToParameter(parameters[0], _document);

        var slot = 1;
        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            slot = BindClientSideBinder(parameters, slot, binder, session);
        }

        parameters[slot].Value = _descriptor.Identification.ToRawSqlValue(_id);
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(_descriptor.Identification.RawSqlType);
        slot++;

        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
            slot++;
        }

        foreach (var pk in _descriptor.PartitionPkBinders)
        {
            pk.BindParameter(parameters[slot], _document, session);
            slot++;
        }

        return slot;
    }

    /// <summary>
    /// Bind a single client-side write binder; concurrency-aware
    /// subclasses override this to special-case the VersionBinder /
    /// RevisionBinder.
    /// </summary>
    protected abstract int BindClientSideBinder(NpgsqlParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session);

    public bool TryTransform(System.Exception original, out System.Exception? transformed)
        => ClosedShapeOperationExceptionTransform.TryTransform(original, _descriptor.TableName, typeof(TDoc), _id!, out transformed);
}

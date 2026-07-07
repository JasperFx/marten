#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Weasel.Core;
using Weasel.Postgresql;

using Marten.Internal.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Abstract base for the per-<see cref="ConcurrencyMode"/> closed-shape
/// Overwrite operation. Same shape as Upsert but the trailing
/// concurrency guard is dropped — the caller has explicitly opted to
/// bypass the optimistic / numeric check
/// (<c>session.Store(doc, ignoreConcurrencyCheck: true)</c> or
/// <see cref="Marten.Services.ConcurrencyChecks.Disabled"/>). Sealed
/// subclasses provide the concrete <see cref="ConfigureCommand"/> +
/// <see cref="PostprocessAsync"/> bodies so the hot path doesn't branch
/// on <c>ConcurrencyMode</c> (#4659).
/// </summary>
internal abstract class ClosedShapeOverwriteOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation, IIdentifiedOperation<TDoc, TId>, JasperFx.Core.Exceptions.IExceptionTransform
    where TDoc : notnull
    where TId : notnull
{
    protected readonly TDoc _document;
    protected readonly TId _id;
    protected readonly string _tenantId;
    protected readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    protected ClosedShapeOverwriteOperation(
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

    public IChangeTracker ToTracker(IStorageSession session)
        => new ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Update;

    public abstract void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    public abstract Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);

    /// <summary>
    /// Bind <c>[tenant_id,] id, data</c> + the client-side write binders
    /// up to (not including) the trailing ON CONFLICT SET concurrency
    /// slots. Returns the next free parameter slot.
    /// </summary>
    protected int BindPreOnConflictParameters(DbParameter[] parameters, IStorageSession session)
    {
        var slot = 0;
        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            _descriptor.Dialect.SetParameterType(parameters[slot], StorageColumnType.String);
            slot++;
        }

        parameters[slot].Value = _descriptor.Identification.ToRawSqlValue(_id);
        _descriptor.Dialect.SetIdParameterType(parameters[slot], _descriptor.Identification.RawSqlType);
        slot++;

        foreach (var binder in _descriptor.WriteBinders)
        {
            binder.ApplyToDocument(_document, session);
        }

        session.Serializer.WriteToParameter(parameters[slot], _document);
        slot++;

        foreach (var binder in _descriptor.ClientSideWriteBinders)
        {
            slot = BindClientSideBinder(parameters, slot, binder, session);
        }

        return slot;
    }

    /// <summary>
    /// Bind the optional <c>id [, tenant_id]</c> subquery slots that
    /// <c>UseVersionFromMatchingStream</c> emits inside the revision CASE
    /// expression.
    /// </summary>
    protected int BindUseVersionFromMatchingStreamSubquery(DbParameter[] parameters, int slot)
    {
        parameters[slot].Value = _descriptor.Identification.ToRawSqlValue(_id);
        _descriptor.Dialect.SetIdParameterType(parameters[slot], _descriptor.Identification.RawSqlType);
        slot++;

        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            _descriptor.Dialect.SetParameterType(parameters[slot], StorageColumnType.String);
            slot++;
        }

        return slot;
    }

    /// <summary>
    /// Concurrency-aware subclasses override to special-case the
    /// VersionBinder / RevisionBinder.
    /// </summary>
    protected abstract int BindClientSideBinder(DbParameter[] parameters, int slot, IDocumentMetadataBinder<TDoc> binder, IStorageSession session);

    public bool TryTransform(System.Exception original, out System.Exception? transformed)
        => ClosedShapeOperationExceptionTransform.TryTransform(original, _descriptor.TableName, typeof(TDoc), _id!, out transformed);
}

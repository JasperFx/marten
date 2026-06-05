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
/// Insert operation. Holds the shared infrastructure (document, identity,
/// tenant, descriptor, interface boilerplate) but pushes the parameter-
/// binding and postprocess decisions onto sealed concurrency-specific
/// subclasses so the hot path doesn't read <c>ConcurrencyMode</c> at
/// runtime (#4659).
/// </summary>
/// <remarks>
/// <para>
/// Subclasses:
/// <list type="bullet">
/// <item><see cref="UnversionedClosedShapeInsertOperation{TDoc, TId}"/> —
///       <c>ConcurrencyMode.Off</c>; no version/revision binders to special-case.</item>
/// <item><see cref="OptimisticClosedShapeInsertOperation{TDoc, TId}"/> —
///       <c>ConcurrencyMode.Optimistic</c>; binds + tracks a Guid version
///       per row.</item>
/// <item><see cref="NumericClosedShapeInsertOperation{TDoc, TId}"/> —
///       <c>ConcurrencyMode.Numeric</c>; binds a (2–4) revision-CASE block
///       and tracks the returned revision.</item>
/// </list>
/// </para>
/// </remarks>
internal abstract class ClosedShapeInsertOperation<TDoc, TId>: IDocumentStorageOperation, IRevisionedOperation, IIdentifiedOperation<TDoc, TId>, JasperFx.Core.Exceptions.IExceptionTransform
    where TDoc : notnull
    where TId : notnull
{
    protected readonly TDoc _document;
    protected readonly TId _id;
    protected readonly string _tenantId;
    protected readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    protected ClosedShapeInsertOperation(
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

    public Marten.Internal.DirtyTracking.IChangeTracker ToTracker(IMartenSession session)
        => new Marten.Internal.DirtyTracking.ChangeTracker<TDoc>(session, _document);

    public OperationRole Role() => OperationRole.Insert;

    public abstract void ConfigureCommand(ICommandBuilder builder, IMartenSession session);

    public abstract Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);

    /// <summary>
    /// Bind the leading <c>[tenant_id, ] id, data</c> parameter triple and
    /// project session-derived metadata onto the document before
    /// serialization. Returns the next free parameter slot.
    /// </summary>
    /// <remarks>
    /// Mirrors the codegen path's GenerateCodeToModifyDocument frames:
    /// Correlation / Causation / Headers / LastModifiedBy etc. land on
    /// the document so they flow into the JSON data column too.
    /// </remarks>
    protected int BindLeadingParameters(NpgsqlParameter[] parameters, IMartenSession session)
    {
        var slot = 0;
        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
            slot++;
        }

        parameters[slot].Value = _descriptor.Identification.ToRawSqlValue(_id);
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(_descriptor.Identification.RawSqlType);
        slot++;

        foreach (var binder in _descriptor.WriteBinders)
        {
            binder.ApplyToDocument(_document, session);
        }

        session.Serializer.WriteToParameter(parameters[slot], _document);
        slot++;

        return slot;
    }

    /// <summary>
    /// Bind the optional <c>id [, tenant_id]</c> subquery slots that
    /// <c>UseVersionFromMatchingStream</c> emits inside the revision
    /// CASE expression. Returns the next free slot. Common to the
    /// Numeric variants of Insert / Upsert / Overwrite.
    /// </summary>
    protected int BindUseVersionFromMatchingStreamSubquery(NpgsqlParameter[] parameters, int slot)
    {
        parameters[slot].Value = _descriptor.Identification.ToRawSqlValue(_id);
        parameters[slot].NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(_descriptor.Identification.RawSqlType);
        slot++;

        if (_descriptor.IsConjoined)
        {
            parameters[slot].Value = _tenantId;
            parameters[slot].NpgsqlDbType = NpgsqlDbType.Varchar;
            slot++;
        }

        return slot;
    }

    public bool TryTransform(System.Exception original, out System.Exception? transformed)
        => ClosedShapeOperationExceptionTransform.TryTransform(original, _descriptor.TableName, typeof(TDoc), _id!, out transformed);
}

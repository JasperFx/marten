#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike: a hand-written, closed-shape <see cref="DocumentStorage{T, TId}"/>
/// subclass that proves the closed-shape pattern can be driven end-to-end
/// without runtime Roslyn codegen. Composes with
/// <see cref="IIdentification{TDoc, TId}"/> for the identity surface and
/// returns hand-written <see cref="ClosedShapeUpsertOperation{TDoc}"/>
/// instances for write operations.
/// </summary>
/// <remarks>
/// <para>
/// This is ONE cell of the planned W3 matrix: Lightweight + Guid + no
/// concurrency + no revisions + no tenancy + no hierarchical + minimal
/// metadata. Demonstrates the pattern; production hand-write covers ~24
/// cells (StorageStyle × Concurrency × Hierarchical).
/// </para>
/// <para>
/// Inheriting <see cref="LightweightDocumentStorage{T, TId}"/> picks up
/// the Store / Eject / LoadAsync / LoadManyAsync plumbing for free. What
/// we hand-write here: Identity / AssignIdentity (via IIdentification),
/// Insert / Update / Upsert (return ClosedShapeUpsertOperation), Overwrite
/// (throws — out of scope), and BuildSelector (returns the existing
/// <see cref="SerializationSelector{T}"/>).
/// </para>
/// </remarks>
public sealed class LightweightSequentialGuidStorage<TDoc>: LightweightDocumentStorage<TDoc, Guid>
    where TDoc : notnull
{
    private readonly IIdentification<TDoc, Guid> _identification;
    private readonly string _upsertSqlPrefix;
    private readonly string _upsertSqlSuffix;

    public LightweightSequentialGuidStorage(DocumentMapping mapping, IIdentification<TDoc, Guid> identification)
        : base(mapping)
    {
        _identification = identification;

        // Pre-built once at construction — closed-shape SQL strings are
        // readonly fields on the storage instance, not concatenated per
        // call. Matches W3's "minimize per-call string concatenation"
        // constraint.
        _upsertSqlPrefix = $"insert into {mapping.TableName.QualifiedName} (id, data) values (";
        _upsertSqlSuffix = ") on conflict (id) do update set data = excluded.data";
    }

    public override Guid Identity(TDoc document)
        => _identification.Identity(document);

    public override Guid AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _identification.AssignIfMissing(document, database);

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => Upsert(document, session, tenant);

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => Upsert(document, session, tenant);

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpsertOperation<TDoc>(
            document, Identity(document), _upsertSqlPrefix, _upsertSqlSuffix, OperationRole.Upsert);

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException(
            $"{nameof(LightweightSequentialGuidStorage<TDoc>)} doesn't implement Overwrite — out of W3 spike scope. " +
            "Add when wiring optimistic concurrency / revisions.");

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeLightweightSelector<TDoc>(session.Serializer);
}

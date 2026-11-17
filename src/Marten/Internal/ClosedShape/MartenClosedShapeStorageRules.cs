#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon.Internals;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.ClosedShape;

/// <summary>
///     Marten's half of Weasel's closed-shape storage family (#5733, weasel#811): everything a storage
///     needs that is Marten's rather than the family's — the read SQL per tracking mode, the delete
///     fragments, the soft-delete and conjoined-tenancy filters, the id setters, the mapped version and
///     revision members, the daemon's shared projection session and version-tracker sharing for nested
///     sessions. Read once off the <see cref="DocumentMapping" /> when the storage is built, so no
///     mapping is retained (#4828). Also carries the LINQ member model and metadata columns the
///     Marten storages expose through <see cref="ILinqDocumentStorage" />.
/// </summary>
internal sealed class MartenClosedShapeStorageRules<TDoc, TId>: IClosedShapeStorageRules<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly DeleteStyle _deleteStyle;
    private readonly ISqlFragment? _defaultWhere;
    private readonly ClosedShapeReadSql[] _readSql = new ClosedShapeReadSql[4];
    private readonly bool _useNumericRevisions;

    // Null for an id member with no setter (an F# record, say); SetIdentity is then a no-op, as it
    // always was, so a projection's metadata pass over such an aggregate does not fail.
    private readonly Action<TDoc, TId>? _setter;
    private readonly Action<TDoc, string> _setFromString = (_, _) => throw new NotSupportedException();
    private readonly Action<TDoc, Guid> _setFromGuid = (_, _) => throw new NotSupportedException();

    // #5372: compiled accessors for a version / revision member mapped with
    // Metadata.Version.MapTo(...) / Metadata.Revision.MapTo(...), null when the mapping has none.
    private readonly Func<TDoc, Guid>? _mappedVersionGetter;
    private readonly Func<TDoc, long>? _mappedRevisionGetter;

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (LambdaBuilder / FastExpressionCompiler, ValueTypeIdGeneration wrappers). Document types flow in from StoreOptions / Schema.For<T>() and are preserved per the AOT publishing guide.")]
    public MartenClosedShapeStorageRules(DocumentMapping document)
    {
        TableName = document.TableName;
        TenancyStyle = document.TenancyStyle;
        DuplicatedFields = document.DuplicatedFields;
        QueryMembers = document.QueryMembers;
        MetadataColumns = document.Schema.Table.Columns.OfType<MetadataColumn>().ToArray();
        TruncateSql = $"truncate {TableName.QualifiedName} cascade";

        _deleteStyle = document.DeleteStyle;
        _useNumericRevisions = document.UseNumericRevisions;

        var defaults = defaultFilters().ToArray();
        _defaultWhere = defaults.Length switch
        {
            0 => null,
            1 => defaults[0],
            _ => CompoundWhereFragment.And(defaults)
        };

        var table = document.Schema.Table;
        foreach (var mode in Enum.GetValues<ClosedShapeTrackingMode>())
        {
            var selectFields = table.SelectColumns(storageStyleFor(mode)).Select(x => $"d.{x.Name}").ToArray();
            var fieldSelector = selectFields.Join(", ");

            var loaderSql = $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = $1";
            var loadArraySql =
                $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = ANY($1)";

            if (TenancyStyle == TenancyStyle.Conjoined)
            {
                loaderSql += $" and d.{TenantIdColumn.Name} = $2";
                loadArraySql += $" and d.{TenantIdColumn.Name} = $2";
            }

            _readSql[(int)mode] = new ClosedShapeReadSql(selectFields,
                $"select {fieldSelector} from {document.TableName.QualifiedName} as d", loaderSql, loadArraySql);
        }

        // The gating mirrors ClosedShapeBulkLoader rather than the narrower UseNumericRevisions check:
        // Metadata.Revision.Enabled is also set by Revision.MapTo(...) on its own, and a mapping cannot
        // have both enabled -- they are the same physical mt_version column.
        if (document.UseNumericRevisions || document.Metadata.Revision.Enabled)
        {
            if (document.Metadata.Revision.Member is { } revisionMember)
            {
                // The column is bigint or integer, but the member is int (IRevisioned) or long
                // (ILongVersioned). Widen an int member the way DocumentRevisionBinder narrows on the
                // way back in.
                if (revisionMember.GetRawMemberType() == typeof(int))
                {
                    var intGetter = LambdaBuilder.Getter<TDoc, int>(revisionMember);
                    _mappedRevisionGetter = doc => intGetter(doc);
                }
                else
                {
                    _mappedRevisionGetter = LambdaBuilder.Getter<TDoc, long>(revisionMember);
                }
            }
        }
        else if (document.Metadata.Version.Member is { } versionMember)
        {
            _mappedVersionGetter = LambdaBuilder.Getter<TDoc, Guid>(versionMember);
        }

        _setter = LambdaBuilder.Setter<TDoc, TId>(document.IdMember);
        if (typeof(TId) == typeof(Guid))
        {
            _setFromGuid = _setter.As<Action<TDoc, Guid>>();
        }
        else if (typeof(TId) == typeof(string))
        {
            _setFromString = _setter.As<Action<TDoc, string>>();
        }
        else if (document.IdStrategy is ValueTypeIdGeneration valueType)
        {
            if (valueType.SimpleType == typeof(Guid))
            {
                var converter = valueType.CreateWrapper<TId, Guid>();
                _setFromGuid = (doc, guid) => _setter?.Invoke(doc, converter(guid));
            }
            else if (valueType.SimpleType == typeof(string))
            {
                var converter = valueType.CreateWrapper<TId, string>();
                _setFromString = (doc, s) => _setter?.Invoke(doc, converter(s));
            }
        }
    }

    /// <summary>The LINQ member model, for <see cref="ILinqDocumentStorage.QueryMembers" />.</summary>
    public IQueryableMemberCollection QueryMembers { get; }

    /// <summary>The table's metadata columns, for <see cref="IHaveMetadataColumns" />.</summary>
    public MetadataColumn[] MetadataColumns { get; }

    public DbObjectName TableName { get; }

    public TenancyStyle TenancyStyle { get; }

    public IReadOnlyList<IDuplicatedField> DuplicatedFields { get; }

    public string TruncateSql { get; }

    public ClosedShapeReadSql ReadSqlFor(ClosedShapeTrackingMode mode) => _readSql[(int)mode];

    private static StorageStyle storageStyleFor(ClosedShapeTrackingMode mode) => mode switch
    {
        ClosedShapeTrackingMode.QueryOnly => StorageStyle.QueryOnly,
        ClosedShapeTrackingMode.Lightweight => StorageStyle.Lightweight,
        ClosedShapeTrackingMode.IdentityMap => StorageStyle.IdentityMap,
        ClosedShapeTrackingMode.DirtyTracking => StorageStyle.DirtyTracking,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public IOperationFragment BuildDeleteFragment(IDocumentStorage<TDoc, TId> storage)
        => _deleteStyle == DeleteStyle.Remove ? new HardDelete(storage) : new SoftDelete(storage);

    public IOperationFragment BuildHardDeleteFragment(IDocumentStorage<TDoc, TId> storage) => new HardDelete(storage);

    public IDeletion BuildDeletion(IDocumentStorage<TDoc, TId> storage, IOperationFragment fragment, TId id,
        Weasel.Core.SqlGeneration.ISqlFragment byIdFilter, string tenantId)
    {
        // The neutral id filter is always a Marten (Postgres) ByIdFilter underneath.
        var filter = (ISqlFragment)byIdFilter;

        return TenancyStyle == TenancyStyle.Conjoined
            ? new Deletion(storage, fragment, CompoundWhereFragment.And(new SpecificTenantFilter(tenantId), filter))
            : new Deletion(storage, fragment, filter);
    }

    public Weasel.Core.SqlGeneration.ISqlFragment FilterDocuments(Weasel.Core.SqlGeneration.ISqlFragment query,
        IStorageSession session)
    {
        // Neutral (Weasel.Core) contract; Marten's filters are always Postgres fragments underneath.
        var pgQuery = (ISqlFragment)query;
        var extras = extraFilters(pgQuery, session).ToList();

        if (extras.Count > 0)
        {
            extras.Add(pgQuery);
            return CompoundWhereFragment.And(extras);
        }

        return pgQuery;
    }

    public Weasel.Core.SqlGeneration.ISqlFragment? DefaultWhereFragment() => _defaultWhere;

    public void SetIdentity(TDoc document, TId identity) => _setter?.Invoke(document, identity);

    public void SetIdentityFromString(TDoc document, string identityString) => _setFromString(document, identityString);

    public void SetIdentityFromGuid(TDoc document, Guid identityGuid) => _setFromGuid(document, identityGuid);

    public Guid? MappedVersionFor(TDoc document) => _mappedVersionGetter?.Invoke(document);

    public long? MappedRevisionFor(TDoc document) => _mappedRevisionGetter?.Invoke(document);

    /// <summary>
    ///     #4838: the daemon's slice workers share ONE <see cref="ProjectionDocumentSession" />, so a
    ///     lightweight read there gets the selector that leaves the session's version state alone.
    /// </summary>
    public bool IsSharedProjectionSession(IStorageSession session) => session is ProjectionDocumentSession;

    /// <summary>
    ///     A nested tenant session shares the parent's identity map, so it has to share the versions
    ///     recorded against those documents too, or a concurrency check in one would not see the other's.
    /// </summary>
    public void ShareVersions(IStorageSession parent, IStorageSession nested)
    {
        if (parent.Versions is not VersionTracker parentVersions ||
            nested.Versions is not VersionTracker nestedVersions)
        {
            return;
        }

        if (ReferenceEquals(parentVersions, nestedVersions))
        {
            return;
        }

        if (!parentVersions.ByType.TryGetValue(typeof(TDoc), out var versions))
        {
            versions = _useNumericRevisions
                ? new Dictionary<TId, long>()
                : new Dictionary<TId, Guid>();

            parentVersions.ByType[typeof(TDoc)] = versions;
        }

        nestedVersions.ByType[typeof(TDoc)] = versions;
    }

    private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query, IStorageSession session)
    {
        if (_deleteStyle == DeleteStyle.SoftDelete && !query.ContainsAny<ISoftDeletedFilter>())
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
        {
            yield return new DefaultTenantFilter(session.TenantId);
        }
    }

    private IEnumerable<ISqlFragment> defaultFilters()
    {
        if (_deleteStyle == DeleteStyle.SoftDelete)
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            yield return CurrentTenantFilter.Instance;
        }
    }
}

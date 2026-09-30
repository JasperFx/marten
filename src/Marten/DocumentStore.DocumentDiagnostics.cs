#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Documents;
using JasperFx.MultiTenancy;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;

namespace Marten;

public partial class DocumentStore : IDocumentStoreDiagnostics
{
    /// <summary>
    /// Store-agnostic, read-only document-query surface for monitoring consoles
    /// (CritterWatch #545). Mirrors the role <see cref="JasperFx.Events.IEventStore"/>
    /// plays for event streams: list the mapped document types, page their stored
    /// rows as raw JSON, and fetch one by id — all without the console referencing
    /// Marten directly. Reads the canonical <c>data</c> jsonb column straight off
    /// the document table so the JSON matches exactly what Marten persisted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// jasperfx#870 (#5543) turned five of these behaviours from "whatever each store happened to do" into
    /// the contract, and Marten was on the wrong side of all five: soft-deleted rows came back as live,
    /// every sub-class in a hierarchy's table came back whatever type was asked for, a missing tenant read
    /// EVERY tenant, an empty-string tenant read a tenant literally named <c>""</c> (CritterWatch#1304),
    /// and ids were matched as <c>id::text = @id</c> — which cannot use the primary-key index and misses an
    /// upper-case Guid outright. <c>DocumentStoreDiagnosticsCompliance</c> is where all of that is pinned
    /// now, for Marten, Polecat and Fisher alike.
    /// </para>
    /// <para>
    /// <see cref="Subject"/> is not declared here: <c>DocumentStore</c> already carries one public
    /// <c>Uri Subject</c> (DocumentStore.EventStore.cs), which satisfies <see cref="IDocumentStoreDiagnostics"/>,
    /// <see cref="IDocumentStoreDiagnosticsWriter"/>, <c>IEventStore</c> and
    /// <c>IDocumentStoreUsageSource</c> together — exactly as the contract's remarks anticipate. Adding an
    /// explicit <c>Uri IDocumentStoreDiagnostics.Subject</c> here would hide the ancillary-store override
    /// SecondaryStoreConfig applies to that property.
    /// </para>
    /// </remarks>
    async Task<IReadOnlyList<DocumentTypeRef>> IDocumentStoreDiagnostics.DocumentTypesAsync(
        CancellationToken token)
    {
        // Match TryCreateUsage: force lazy mappings to materialize before enumerating.
        Options.Storage.BuildAllMappings();

        var refs = Options.Storage.DocumentMappingsWithSchema
            .OrderBy(x => x.Alias)
            .Select(m => new DocumentTypeRef(m.DocumentType.FullNameInCode(), m.Alias, m.DatabaseSchemaName))
            .ToList();

        return await Task.FromResult<IReadOnlyList<DocumentTypeRef>>(refs).ConfigureAwait(false);
    }

    async Task<DocumentQueryResult> IDocumentStoreDiagnostics.QueryDocumentsAsync(
        string documentTypeName, DocumentQueryOptions options, CancellationToken token)
    {
        // Refused BEFORE anything else, including resolving the type: silently returning the unfiltered
        // page is the one answer a console cannot tell apart from a filter that matched every row
        // (jasperfx#870 §1). Marten will apply these once jasperfx#869's Dynamic LINQ translation lands.
        refuseUnsupportedCriteria(options);

        var pageNumber = Math.Max(1, options.PageNumber);
        var pageSize = Math.Max(1, options.PageSize);

        var target = resolveDiagnosticsTarget(documentTypeName);
        if (target == null)
        {
            return emptyPage(pageNumber, pageSize);
        }

        var database = await findDiagnosticsDatabaseAsync(options.TenantId).ConfigureAwait(false);
        if (database == null)
        {
            return emptyPage(pageNumber, pageSize);
        }

        var predicate = new DiagnosticsPredicate(target, options.TenantId);
        if (options.IdEquals != null && !predicate.TryMatchId(options.IdEquals))
        {
            // An id that cannot be converted to the stored identity type matches nothing, which is the
            // same answer the contract gives for an unknown type name -- never an exception.
            return emptyPage(pageNumber, pageSize);
        }

        predicate.ExcludeSoftDeleted = !options.IncludeSoftDeleted;
        predicate.AddMetadataFilters(options);

        var reader = new StoredDocumentReader(target);
        var offset = (pageNumber - 1) * pageSize;

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        long total;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"select count(*) from {target.Table}{predicate.Sql}";
            predicate.Bind(countCmd);

            total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
        }

        var rows = new List<StoredDocument>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                $"select {reader.SelectList} from {target.Table}{predicate.Sql} order by id limit {pageSize} offset {offset}";
            predicate.Bind(cmd);

            await using var dbReader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await dbReader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(await reader.ReadAsync(dbReader, predicate.TenantId, token).ConfigureAwait(false));
            }
        }

        return new DocumentQueryResult(rows, total, pageNumber, pageSize);
    }

    async Task<StoredDocument?> IDocumentStoreDiagnostics.LoadDocumentAsync(
        string documentTypeName, string id, string? tenantId, CancellationToken token)
    {
        var target = resolveDiagnosticsTarget(documentTypeName);
        if (target == null)
        {
            return null;
        }

        var database = await findDiagnosticsDatabaseAsync(tenantId).ConfigureAwait(false);
        if (database == null)
        {
            return null;
        }

        var predicate = new DiagnosticsPredicate(target, tenantId);
        if (!predicate.TryMatchId(id))
        {
            return null;
        }

        // Deliberately NOT excluding soft-deleted rows: a load by id is an explicit request for that
        // document, so the contract answers with the row flagged (StoredDocument.IsDeleted) rather than
        // pretending it is gone. That is the asymmetry with QueryDocumentsAsync, and it is the whole
        // point -- a console showing a soft-deleted row as missing cannot explain why an id "vanished".
        var reader = new StoredDocumentReader(target);

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select {reader.SelectList} from {target.Table}{predicate.Sql} limit 1";
        predicate.Bind(cmd);

        await using var dbReader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await dbReader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        return await reader.ReadAsync(dbReader, predicate.TenantId, token).ConfigureAwait(false);
    }

    private static DocumentQueryResult emptyPage(int pageNumber, int pageSize)
        => new(Array.Empty<StoredDocument>(), 0, pageNumber, pageSize);

    private static void refuseUnsupportedCriteria(DocumentQueryOptions options)
    {
        if (options.Where != null)
        {
            throw new DocumentCriteriaNotSupportedException(nameof(DocumentQueryOptions.Where),
                "Marten does not translate Dynamic LINQ predicates for diagnostic document queries yet (jasperfx#869). Narrow the page with IdEquals, TenantId or the metadata filters instead.");
        }

        if (options.OrderBy != null)
        {
            throw new DocumentCriteriaNotSupportedException(nameof(DocumentQueryOptions.OrderBy),
                "Marten does not translate Dynamic LINQ ordering for diagnostic document queries yet (jasperfx#869). Results come back in the store's own stable order, by id.");
        }
    }

    /// <summary>
    /// The physical database a diagnostic read should run against, or null when the tenant is not one this
    /// store knows.
    /// </summary>
    /// <remarks>
    /// <c>TryFindDatabase</c>, never <c>FindOrCreateDatabase</c> — the latter PROVISIONS (a
    /// <c>CREATE DATABASE</c> on SingleServerMultiTenancy, shard assignment plus partition DDL on
    /// ShardedTenancy), so asking a read-only console question about an unknown tenant would create that
    /// tenant. #5400 is the same trap on the event-store explorer, and findExplorerDatabaseAsync is the
    /// pattern this follows — except that a diagnostics read answers "nothing here" rather than throwing,
    /// which is how the rest of this contract treats something it cannot find.
    /// </remarks>
    private async ValueTask<IMartenDatabase?> findDiagnosticsDatabaseAsync(string? tenantId)
    {
        if (Tenancy.Cardinality == DatabaseCardinality.Single)
        {
            return Tenancy.Default.Database;
        }

        var normalized = DocumentQueryOptions.NormalizeTenantId(tenantId);
        if (normalized == null)
        {
            return Tenancy.Default.Database;
        }

        return await Tenancy.TryFindDatabase(normalized).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a document type name to the table that holds it plus the type actually asked for — which is
    /// not always the mapped root. A registered sub-class shares its root's table, so naming
    /// <c>ComplianceTruck</c> has to find <c>ComplianceVehicle</c>'s mapping and remember that only the
    /// truck rows were wanted.
    /// </summary>
    private DiagnosticsTarget? resolveDiagnosticsTarget(string documentTypeName)
    {
        Options.Storage.BuildAllMappings();

        var mappings = Options.Storage.DocumentMappingsWithSchema.ToArray();

        var root = mappings.FirstOrDefault(m => namesMatch(m.DocumentType, m.Alias, documentTypeName));
        if (root != null)
        {
            return new DiagnosticsTarget(root, root.DocumentType, null);
        }

        // Sub-classes are searched second so a root always wins its own name, even in the pathological
        // case of a sub-class aliased to a sibling root's alias.
        foreach (var mapping in mappings)
        {
            var subClass = mapping.SubClasses
                .FirstOrDefault(x => namesMatch(x.DocumentType, x.Alias, documentTypeName));

            if (subClass != null)
            {
                return new DiagnosticsTarget(mapping, subClass.DocumentType, subClass.Aliases.ToArray());
            }
        }

        return null;
    }

    private static bool namesMatch(Type documentType, string alias, string documentTypeName)
        => string.Equals(documentType.FullNameInCode(), documentTypeName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(documentType.FullName, documentTypeName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(documentType.Name, documentTypeName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(alias, documentTypeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a diagnostic read is pointed at: the table's mapping, the type the caller named, and — when
    /// that type is a sub-class — the <c>mt_doc_type</c> aliases its rows carry.
    /// </summary>
    internal sealed class DiagnosticsTarget
    {
        public DiagnosticsTarget(DocumentMapping mapping, Type requestedType, string[]? subClassAliases)
        {
            Mapping = mapping;
            RequestedType = requestedType;
            SubClassAliases = subClassAliases;
        }

        public DocumentMapping Mapping { get; }
        public Type RequestedType { get; }

        /// <summary>Null when the root was named, which means every row in the table qualifies.</summary>
        public string[]? SubClassAliases { get; }

        public string Table => Mapping.TableName.QualifiedName;

        public bool IsConjoined => Mapping.TenancyStyle == TenancyStyle.Conjoined;

        public bool IsSoftDeleted => Mapping.DeleteStyle == DeleteStyle.SoftDelete;

        public bool IsHierarchy => Mapping.IsHierarchy();
    }

    /// <summary>
    /// The WHERE clause and its parameters for one diagnostic read, assembled so the count query and the
    /// page query cannot drift apart — the soft-delete and hierarchy predicates have to apply to BOTH, or
    /// <c>TotalCount</c> reports rows the page does not contain.
    /// </summary>
    internal sealed class DiagnosticsPredicate
    {
        private readonly DiagnosticsTarget _target;
        private readonly List<string> _conditions = new();
        private readonly List<Action<NpgsqlCommand>> _parameters = new();
        private object? _id;

        public DiagnosticsPredicate(DiagnosticsTarget target, string? tenantId)
        {
            _target = target;

            // NormalizeTenantId is the one definition every store applies: null, empty and whitespace all
            // mean the DEFAULT tenant. Not "every tenant" (what Marten did before #5543, so a console with
            // no tenant selected saw every tenant's rows folded together), and not a tenant named ""
            // (CritterWatch#1304 measured that reading zero rows on conjoined Marten).
            TenantId = DocumentQueryOptions.NormalizeTenantId(tenantId) ?? StorageConstants.DefaultTenantId;

            if (target.IsConjoined)
            {
                _conditions.Add("tenant_id = @tenant");
                var tenant = TenantId;
                _parameters.Add(cmd => cmd.Parameters.AddWithValue("tenant", tenant));
            }

            if (target.SubClassAliases != null)
            {
                // ANY over every alias the sub-class answers to, because a sub-class that has sub-classes
                // of its own carries all of theirs in Aliases -- asking for a middle of the hierarchy must
                // include its descendants.
                _conditions.Add($"{SchemaConstants.DocumentTypeColumn} = ANY(@doctypes)");
                var aliases = target.SubClassAliases;
                _parameters.Add(cmd => cmd.Parameters.AddWithValue("doctypes", aliases));
            }
        }

        /// <summary>The tenant this read is scoped to, already normalized.</summary>
        public string TenantId { get; }

        public bool ExcludeSoftDeleted { get; set; }

        /// <summary>
        /// Converts the text id to the mapping's stored identity type and adds <c>id = @id</c>, or answers
        /// false when the text is not a value of that type at all.
        /// </summary>
        /// <remarks>
        /// The conversion is the point (jasperfx#870 §3). The old <c>id::text = @id</c> compared PostgreSQL's
        /// canonical rendering of the column against whatever text arrived, so an upper-case Guid never
        /// matched and the primary-key index could not be used for any id type.
        /// </remarks>
        public bool TryMatchId(string idText)
        {
            var idType = _target.Mapping.InnerIdType();

            if (idType == typeof(Guid))
            {
                if (!Guid.TryParse(idText, out var guid)) return false;
                _id = guid;
            }
            else if (idType == typeof(int))
            {
                if (!int.TryParse(idText, out var i)) return false;
                _id = i;
            }
            else if (idType == typeof(long))
            {
                if (!long.TryParse(idText, out var l)) return false;
                _id = l;
            }
            else
            {
                _id = idText;
            }

            _conditions.Add("id = @id");
            var id = _id;
            _parameters.Add(cmd => cmd.Parameters.AddWithValue("id", id!));

            return true;
        }

        /// <summary>
        /// #4791 / CritterWatch #629: exact-match filters on the document's metadata columns. A filter is
        /// honored only when both the option is set AND the mapping has that metadata column enabled —
        /// emitting a WHERE on a column the table does not have would throw 42703 (undefined_column). When
        /// the option is set but the column is disabled the filter is silently skipped, per the
        /// DocumentQueryOptions contract ("Only honored when the store advertises and captures the metadata
        /// column; otherwise ignored").
        /// </summary>
        public void AddMetadataFilters(DocumentQueryOptions options)
        {
            var metadata = _target.Mapping.Metadata;

            if (options.CorrelationId != null && metadata.CorrelationId.Enabled)
            {
                _conditions.Add($"{CorrelationIdColumn.ColumnName} = @corr");
                var value = options.CorrelationId;
                _parameters.Add(cmd => cmd.Parameters.AddWithValue("corr", value));
            }

            if (options.CausationId != null && metadata.CausationId.Enabled)
            {
                _conditions.Add($"{CausationIdColumn.ColumnName} = @caus");
                var value = options.CausationId;
                _parameters.Add(cmd => cmd.Parameters.AddWithValue("caus", value));
            }

            if (options.LastModifiedBy != null && metadata.LastModifiedBy.Enabled)
            {
                _conditions.Add($"{LastModifiedByColumn.ColumnName} = @lmb");
                var value = options.LastModifiedBy;
                _parameters.Add(cmd => cmd.Parameters.AddWithValue("lmb", value));
            }
        }

        public string Sql
        {
            get
            {
                var conditions = _conditions.ToList();
                if (ExcludeSoftDeleted && _target.IsSoftDeleted)
                {
                    conditions.Add($"{SchemaConstants.DeletedColumn} = FALSE");
                }

                return conditions.Count > 0 ? " where " + string.Join(" and ", conditions) : "";
            }
        }

        public void Bind(NpgsqlCommand command)
        {
            foreach (var parameter in _parameters)
            {
                parameter(command);
            }
        }
    }

    /// <summary>
    /// Builds the select list for one mapping and turns each row into a <see cref="StoredDocument"/>.
    /// </summary>
    /// <remarks>
    /// The column list has to be composed per mapping rather than fixed, because Marten only creates the
    /// metadata columns a mapping actually asked for: <c>tenant_id</c> exists only for conjoined tenancy,
    /// <c>mt_doc_type</c> only for a hierarchy, <c>mt_deleted</c> / <c>mt_deleted_at</c> only for a
    /// soft-deleting type, and <c>mt_created_at</c> is opt-in and off by default. Selecting one that is
    /// absent is a 42703, which is why this walks the mapping instead of naming them all.
    /// </remarks>
    internal sealed class StoredDocumentReader
    {
        private readonly DiagnosticsTarget _target;
        private readonly int _versionIndex = -1;
        private readonly int _lastModifiedIndex = -1;
        private readonly int _createdAtIndex = -1;
        private readonly int _tenantIndex = -1;
        private readonly int _deletedIndex = -1;
        private readonly int _deletedAtIndex = -1;
        private readonly int _docTypeIndex = -1;

        public StoredDocumentReader(DiagnosticsTarget target)
        {
            _target = target;

            // id::text so every identity type -- Guid, int, long, string, or a strong-typed wrapper's inner
            // value -- comes back as the text StoredDocument.Id is defined as, without a per-type switch on
            // the read side. data::text guarantees the jsonb column arrives as JSON text.
            var columns = new List<string> { "id::text", "data::text" };
            var metadata = target.Mapping.Metadata;

            // Guid version and numeric revision are the SAME mt_version column -- they are mutually
            // exclusive on a mapping, and Version.Enabled is cleared for a revision-tracked type. Read it
            // as text either way: StoredDocument.Version is explicitly an opaque token the console echoes
            // back, so a uuid and a bigint need no distinguishing.
            if (metadata.Version.Enabled || metadata.Revision.Enabled)
            {
                _versionIndex = columns.Count;
                columns.Add($"{SchemaConstants.VersionColumn}::text");
            }

            if (metadata.LastModified.Enabled)
            {
                _lastModifiedIndex = columns.Count;
                columns.Add(SchemaConstants.LastModifiedColumn);
            }

            if (metadata.CreatedAt.Enabled)
            {
                _createdAtIndex = columns.Count;
                columns.Add(SchemaConstants.CreatedAtColumn);
            }

            if (target.IsConjoined)
            {
                _tenantIndex = columns.Count;
                columns.Add(TenantIdColumn.Name);
            }

            if (target.IsSoftDeleted)
            {
                _deletedIndex = columns.Count;
                columns.Add(SchemaConstants.DeletedColumn);
                _deletedAtIndex = columns.Count;
                columns.Add(SchemaConstants.DeletedAtColumn);
            }

            if (target.IsHierarchy)
            {
                _docTypeIndex = columns.Count;
                columns.Add(SchemaConstants.DocumentTypeColumn);
            }

            SelectList = string.Join(", ", columns);
        }

        public string SelectList { get; }

        public async Task<StoredDocument> ReadAsync(
            System.Data.Common.DbDataReader reader, string scopedTenantId, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var json = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);

            return new StoredDocument(id, json)
            {
                Version = await textAsync(reader, _versionIndex, token).ConfigureAwait(false),
                LastModified = await stampAsync(reader, _lastModifiedIndex, token).ConfigureAwait(false),
                Created = await stampAsync(reader, _createdAtIndex, token).ConfigureAwait(false),
                // A single-tenanted mapping has no tenant_id column at all, so the scope this read ran in
                // is the honest answer -- and the contract requires a value ("the store's default tenant id
                // for a single-tenanted type"), not a null.
                TenantId = _tenantIndex >= 0
                    ? await textAsync(reader, _tenantIndex, token).ConfigureAwait(false) ?? scopedTenantId
                    : scopedTenantId,
                IsDeleted = _deletedIndex >= 0
                            && await reader.GetFieldValueAsync<bool>(_deletedIndex, token).ConfigureAwait(false),
                DeletedAt = await stampAsync(reader, _deletedAtIndex, token).ConfigureAwait(false),
                DocumentType = (await documentTypeAsync(reader, token).ConfigureAwait(false)).FullNameInCode()
            };
        }

        /// <summary>
        /// The row's OWN type, which in a hierarchy is not the type that was asked for: naming the root has
        /// to return every row reporting its own sub-class.
        /// </summary>
        private async Task<Type> documentTypeAsync(System.Data.Common.DbDataReader reader, CancellationToken token)
        {
            if (_docTypeIndex < 0)
            {
                return _target.RequestedType;
            }

            var alias = await textAsync(reader, _docTypeIndex, token).ConfigureAwait(false);
            if (alias == null)
            {
                return _target.RequestedType;
            }

            try
            {
                return _target.Mapping.TypeFor(alias);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A discriminator naming a sub-class this store no longer registers -- rows outlive
                // configuration. Reporting the root beats failing the whole page over one row.
                return _target.Mapping.DocumentType;
            }
        }

        private static async Task<string?> textAsync(
            System.Data.Common.DbDataReader reader, int index, CancellationToken token)
            => index < 0 || await reader.IsDBNullAsync(index, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<string>(index, token).ConfigureAwait(false);

        private static async Task<DateTimeOffset?> stampAsync(
            System.Data.Common.DbDataReader reader, int index, CancellationToken token)
            => index < 0 || await reader.IsDBNullAsync(index, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(index, token).ConfigureAwait(false);
    }
}

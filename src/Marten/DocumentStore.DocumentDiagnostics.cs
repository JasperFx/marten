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

        var refs = new List<DocumentTypeRef>();

        foreach (var mapping in Options.Storage.DocumentMappingsWithSchema.OrderBy(x => x.Alias))
        {
            refs.Add(new DocumentTypeRef(mapping.DocumentType.FullNameInCode(), mapping.Alias,
                mapping.DatabaseSchemaName));

            // jasperfx#932: a sub-class is listed too, because resolveDiagnosticsTarget already accepts
            // its name and narrows to its rows -- a picker that could not offer it would be hiding a
            // capability the contract guarantees. Its rows live in the root's table, so it carries the
            // root's schema and its own mt_doc_type alias, which is the name a caller passes back.
            foreach (var subClass in mapping.SubClasses.OrderBy(x => x.Alias))
            {
                refs.Add(new DocumentTypeRef(subClass.DocumentType.FullNameInCode(), subClass.Alias,
                    mapping.DatabaseSchemaName)
                {
                    RootTypeName = mapping.DocumentType.FullNameInCode()
                });
            }
        }

        return await Task.FromResult<IReadOnlyList<DocumentTypeRef>>(refs).ConfigureAwait(false);
    }

    async Task<DocumentQueryResult> IDocumentStoreDiagnostics.QueryDocumentsAsync(
        string documentTypeName, DocumentQueryOptions options, CancellationToken token)
    {
        // #5544 / jasperfx#928: the request's own shape first, before anything about this store. One read
        // cannot be scoped to one tenant AND to all of them, and a store must not pick -- so this is an
        // ArgumentException on every store, asserted by the compliance suite whether or not the store can
        // honour AllTenants at all.
        options.AssertValidTenantScope();

        var pageNumber = Math.Max(1, options.PageNumber);
        var pageSize = Math.Max(1, options.PageSize);

        var target = resolveDiagnosticsTarget(documentTypeName);
        if (target == null)
        {
            return emptyPage(pageNumber, pageSize);
        }

        var database = options.AllTenants
            ? allTenantsDatabase()
            : await findDiagnosticsDatabaseAsync(options.TenantId).ConfigureAwait(false);

        if (database == null)
        {
            return emptyPage(pageNumber, pageSize);
        }

        // jasperfx#869: Where / OrderBy go through Marten's own LINQ provider. Without them, nothing below
        // this line changed.
        if (options.HasCriteria())
        {
            return await queryWithCriteriaAsync(target, database, options, pageNumber, pageSize, token)
                .ConfigureAwait(false);
        }

        var predicate = new DiagnosticsPredicate(target, options.TenantId, options.AllTenants);
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
                $"select {reader.SelectList} from {target.Table}{predicate.Sql} order by {predicate.OrderBy} limit {pageSize} offset {offset}";
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
    /// <summary>
    /// #5544 / jasperfx#928: the one database an all-tenants read can be answered from, or a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conjoined tenancy keeps every tenant in one database behind a <c>tenant_id</c> column, so dropping
    /// the tenant predicate reads all of them in one query — which is the shape CritterWatch's Document
    /// Explorer actually asks for (CritterWatch#1304).
    /// </para>
    /// <para>
    /// A store that spans several databases is <b>refused</b> rather than answered from the default one.
    /// The contract is explicit that returning the default tenant's rows as though they were every
    /// tenant's is the one failure this flag exists to prevent, and a console cannot tell that apart from
    /// a store with one tenant. Fanning out across the tenant databases is honest but is a different piece
    /// of work — the paging has to stay deterministic across N databases, which means either ordering the
    /// union in the client or a per-database cursor, and neither belongs in the same change as the flag.
    /// </para>
    /// </remarks>
    private IMartenDatabase allTenantsDatabase()
    {
        if (Tenancy.Cardinality != DatabaseCardinality.Single)
        {
            throw new DocumentCriteriaNotSupportedException(nameof(DocumentQueryOptions.AllTenants),
                "this store spreads its tenants across several databases, and Marten does not fan a diagnostic document query out across them yet. Query one tenant at a time with DocumentQueryOptions.TenantId.");
        }

        return Tenancy.Default.Database;
    }

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
        private readonly bool _allTenants;

        public DiagnosticsPredicate(DiagnosticsTarget target, string? tenantId, bool allTenants = false)
        {
            _target = target;
            _allTenants = allTenants;

            // NormalizeTenantId is the one definition every store applies: null, empty and whitespace all
            // mean the DEFAULT tenant. Not "every tenant" (what Marten did before #5543, so a console with
            // no tenant selected saw every tenant's rows folded together), and not a tenant named ""
            // (CritterWatch#1304 measured that reading zero rows on conjoined Marten).
            TenantId = DocumentQueryOptions.NormalizeTenantId(tenantId) ?? StorageConstants.DefaultTenantId;

            // #5544 / jasperfx#928: AllTenants drops the predicate rather than widening it. A
            // single-tenanted mapping has no tenant_id column at all, so it reads exactly as it would with
            // no tenant -- which is what the contract says a single-tenanted type does under AllTenants.
            if (target.IsConjoined && !allTenants)
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
            if (!TryConvertId(_target.Mapping, idText, out var id))
            {
                return false;
            }

            _conditions.Add("id = @id");
            _parameters.Add(cmd => cmd.Parameters.AddWithValue("id", id));

            return true;
        }

        /// <summary>
        /// The text id as a value of the mapping's stored identity type, or false when it is not one.
        /// Shared by the raw read and the criteria read (jasperfx#869) so the two cannot disagree on what
        /// an id matches.
        /// </summary>
        internal static bool TryConvertId(DocumentMapping mapping, string idText, out object id)
        {
            var idType = mapping.InnerIdType();
            id = idText;

            if (idType == typeof(Guid))
            {
                if (!Guid.TryParse(idText, out var guid)) return false;
                id = guid;
            }
            else if (idType == typeof(int))
            {
                if (!int.TryParse(idText, out var i)) return false;
                id = i;
            }
            else if (idType == typeof(long))
            {
                if (!long.TryParse(idText, out var l)) return false;
                id = l;
            }

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
            var index = 0;
            foreach (var (column, value) in MetadataFilters(_target.Mapping, options))
            {
                var name = $"meta{index++}";
                _conditions.Add($"{column} = @{name}");
                _parameters.Add(cmd => cmd.Parameters.AddWithValue(name, value));
            }
        }

        /// <summary>
        /// The metadata column / value pairs <see cref="AddMetadataFilters" /> applies, as data — so the
        /// criteria read (jasperfx#869) applies exactly the same ones.
        /// </summary>
        internal static IEnumerable<(string Column, string Value)> MetadataFilters(DocumentMapping mapping,
            DocumentQueryOptions options)
        {
            var metadata = mapping.Metadata;

            if (options.CorrelationId != null && metadata.CorrelationId.Enabled)
            {
                yield return (CorrelationIdColumn.ColumnName, options.CorrelationId);
            }

            if (options.CausationId != null && metadata.CausationId.Enabled)
            {
                yield return (CausationIdColumn.ColumnName, options.CausationId);
            }

            if (options.LastModifiedBy != null && metadata.LastModifiedBy.Enabled)
            {
                yield return (LastModifiedByColumn.ColumnName, options.LastModifiedBy);
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

        /// <summary>
        /// #5544 / jasperfx#928: tenant first, then the store's own stable order, so an all-tenants page can
        /// never repeat a row another page already returned.
        /// </summary>
        /// <remarks>
        /// Ordering by <c>id</c> alone is NOT enough once several tenants are in play: the same id legitimately
        /// exists in every tenant, so <c>limit/offset</c> over a non-unique sort key can show one tenant's copy
        /// on two pages and another's on none. <c>(tenant_id, id)</c> is the conjoined table's primary key, so
        /// it is both total and index-ordered.
        /// </remarks>
        public string OrderBy
            => _allTenants && _target.IsConjoined ? $"{TenantIdColumn.Name}, id" : "id";

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

        /// <param name="target">What is being read.</param>
        /// <param name="alias">
        /// The table alias to qualify every column with, or null for an unaliased single-table read. The
        /// criteria read (jasperfx#869) passes <c>d</c>, the alias Marten's LINQ statements give the
        /// document table, because its select list is spliced into a statement Marten built.
        /// </param>
        public StoredDocumentReader(DiagnosticsTarget target, string? alias = null)
        {
            _target = target;
            var p = alias == null ? "" : alias + ".";

            // id::text so every identity type -- Guid, int, long, string, or a strong-typed wrapper's inner
            // value -- comes back as the text StoredDocument.Id is defined as, without a per-type switch on
            // the read side. data::text guarantees the jsonb column arrives as JSON text.
            var columns = new List<string> { $"{p}id::text", $"{p}data::text" };
            var metadata = target.Mapping.Metadata;

            // Guid version and numeric revision are the SAME mt_version column -- they are mutually
            // exclusive on a mapping, and Version.Enabled is cleared for a revision-tracked type. Read it
            // as text either way: StoredDocument.Version is explicitly an opaque token the console echoes
            // back, so a uuid and a bigint need no distinguishing.
            if (metadata.Version.Enabled || metadata.Revision.Enabled)
            {
                _versionIndex = columns.Count;
                columns.Add($"{p}{SchemaConstants.VersionColumn}::text");
            }

            if (metadata.LastModified.Enabled)
            {
                _lastModifiedIndex = columns.Count;
                columns.Add(p + SchemaConstants.LastModifiedColumn);
            }

            if (metadata.CreatedAt.Enabled)
            {
                _createdAtIndex = columns.Count;
                columns.Add(p + SchemaConstants.CreatedAtColumn);
            }

            if (target.IsConjoined)
            {
                _tenantIndex = columns.Count;
                columns.Add(p + TenantIdColumn.Name);
            }

            if (target.IsSoftDeleted)
            {
                _deletedIndex = columns.Count;
                columns.Add(p + SchemaConstants.DeletedColumn);
                _deletedAtIndex = columns.Count;
                columns.Add(p + SchemaConstants.DeletedAtColumn);
            }

            if (target.IsHierarchy)
            {
                _docTypeIndex = columns.Count;
                columns.Add(p + SchemaConstants.DocumentTypeColumn);
            }

            SelectFields = columns.ToArray();
            SelectList = string.Join(", ", columns);
        }

        public string[] SelectFields { get; }

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

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Documents;
using JasperFx.Linq;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.MatchesSql;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SoftDeletes;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Util;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten;

public partial class DocumentStore
{
    /// <summary>
    /// jasperfx#869: the Dynamic LINQ shapes Marten refuses for diagnostic document criteria, on top of
    /// JasperFx's own allow-list — one policy for <see cref="DocumentQueryOptions.Where" /> and one for
    /// <see cref="DocumentQueryOptions.OrderBy" />, each built over the document type's own members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule exists only for a shape Marten translates <em>silently wrong</em> — it runs and answers
    /// differently from the same text over LINQ to objects — because every other shape the provider cannot
    /// translate already fails honestly at execution and comes back as
    /// <see cref="DocumentQueryCriteria.Untranslatable" />. Measured against that oracle at the time of
    /// jasperfx#869, under PascalCase / integer enums, camelCase / string enums and duplicated fields
    /// (<c>document_store_diagnostics_criteria_shape_matrix</c> in CoreTests, which pins every verdict):
    /// </para>
    /// <list type="bullet">
    /// <item><b>An enum member stored as its NAME</b> (<c>EnumStorage.AsString</c>, or a duplicated field
    /// created that way) compares and orders alphabetically, where the C# text means by value:
    /// <c>Status &gt; 0</c> returned 4 of the oracle's 8 rows, <c>Status &lt; @0</c> none of 8, and
    /// <c>order by Status</c> put Cancelled before Open. Equality, <c>!=</c> and <c>in @0</c> are unaffected and
    /// stay allowed. The same members stored as integers compare correctly, so the rule resolves the member
    /// through Marten's own query-member model rather than refusing every enum.</item>
    /// <item><b>SQL null semantics</b> (<see cref="DynamicQueryShapeRules.SqlNullSemantics" />, the shared
    /// JasperFx rule): <c>&lt;&gt;</c>, or <c>not</c> over a comparison or string method, on a member that can
    /// be null. Marten translates <c>Notes != 'x'</c> to a plain <c>&lt;&gt;</c>, not <c>IS DISTINCT FROM</c>,
    /// so every null row drops out: <c>Notes != @0</c>, <c>not (Notes = @0)</c> and
    /// <c>not Notes.Contains(@0)</c> returned 3 of the oracle's 9 rows, <c>Discount != @0</c> and
    /// <c>BillTo.City != @0</c> 4 of 8. The guarded forms (<c>… or Notes = null</c>,
    /// <c>Notes != null and …</c>) and <c>!=</c> on a non-nullable member translate correctly and stay
    /// allowed.</item>
    /// </list>
    /// <para>
    /// What did NOT need a rule, against the jasperfx#869 spike's findings on Polecat and Fisher: date member
    /// access (<c>PlacedAt.Year</c>, <c>.Month</c>, <c>.Day</c>, <c>.Date</c>) and <c>string.Length</c> throw
    /// honestly here; <c>Items.Count</c>, <c>Tags.Length</c>, <c>Count()</c>, <c>Any(…)</c>,
    /// <c>Tags.Any(it = …)</c>, decimals, nested members and nulls translate correctly; inline
    /// <c>in (1, 2, 3)</c> still throws (use <c>in @0</c>). Two further differences are corrected rather than
    /// refused, so they cost the operator nothing: null placement in an ordering (PostgreSQL puts nulls last
    /// ascending, C# first — the diagnostic read appends <c>NULLS FIRST</c> / <c>NULLS LAST</c>) and the Kind of
    /// a <see cref="DateTime" /> argument (see <see cref="DateTimeKindAdapter" />).
    /// </para>
    /// </remarks>
    internal (DynamicQueryPolicy Where, DynamicQueryPolicy OrderBy) DiagnosticsCriteriaPolicies(
        IQueryableMemberCollection members, Type documentType)
    {
        var storesAsName = new StringEnumMembers(members, documentType, Options.Serializer().EnumStorage);

        return (
            DynamicQueryPolicy.Default.WithRules(
                DynamicQueryShapeRules.SqlNullSemantics(),
                DynamicQueryShapeRules.For(storesAsName.RefuseRangeComparison)),
            DynamicQueryPolicy.Default.WithRules(DynamicQueryShapeRules.For(storesAsName.RefuseOrdering)));
    }

    /// <summary>
    /// Answers whether an enum member in criteria text is stored as its name — by asking Marten's query-member
    /// model, which is what the provider will translate it with.
    /// </summary>
    internal sealed class StringEnumMembers
    {
        private readonly IQueryableMemberCollection _members;
        private readonly Type _documentType;
        private readonly EnumStorage _serializerEnumStorage;

        public StringEnumMembers(IQueryableMemberCollection members, Type documentType, EnumStorage serializerEnumStorage)
        {
            _members = members;
            _documentType = documentType;
            _serializerEnumStorage = serializerEnumStorage;
        }

        public string? RefuseRangeComparison(Expression node)
        {
            if (node is not BinaryExpression
                {
                    NodeType: ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan
                    or ExpressionType.LessThanOrEqual
                } binary)
            {
                return null;
            }

            var member = enumMember(binary.Left) ?? enumMember(binary.Right);
            return member != null && isStoredAsName(member)
                ? $"compares the enum member {member.Member.Name} with an ordering operator, but this store keeps it as its name, so the comparison would be alphabetical rather than by value. Use = / != or 'in @0' with the values you want."
                : null;
        }

        public string? RefuseOrdering(Expression node)
            => enumMember(node) is { } member && isStoredAsName(member)
                ? $"orders by the enum member {member.Member.Name}, which this store keeps as its name, so the order would be alphabetical rather than by value. Order by another member, or filter on it with = / 'in @0'."
                : null;

        private static MemberExpression? enumMember(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            {
                expression = convert.Operand;
            }

            return expression is MemberExpression member
                   && (member.Type.IsEnum || Nullable.GetUnderlyingType(member.Type)?.IsEnum == true)
                ? member
                : null;
        }

        private bool isStoredAsName(MemberExpression member)
        {
            // A member reached from the document itself resolves through the mapping, which knows about
            // duplicated columns and their own enum storage. One reached inside a collection lambda
            // (Items.Any(Kind > 1)) lives in the JSON, which the serializer wrote.
            if (rootParameterType(member) is { } root && root.IsAssignableFrom(_documentType))
            {
                try
                {
                    return _members.MemberFor(member) switch
                    {
                        EnumAsStringMember => true,
                        DuplicatedField duplicated => duplicated.DbType == NpgsqlTypes.NpgsqlDbType.Varchar,
                        _ => false
                    };
                }
                catch (Exception)
                {
                    // Not a member Marten can locate; fall through to what the serializer would have written.
                }
            }

            return _serializerEnumStorage == EnumStorage.AsString;
        }

        private static Type? rootParameterType(MemberExpression member)
        {
            Expression? current = member;
            while (current is MemberExpression m)
            {
                current = m.Expression;
            }

            return (current as ParameterExpression)?.Type;
        }
    }

    // #5619 house style: resolved by name with RequireMethod (a rename fails loudly, by name), and behind a
    // Lazy so the lookup is deferred to the first criteria read -- every_reflective_method_handle_resolves
    // forces .Value, so a refactor that loses the method fails that test rather than a console's query.
    private static readonly Lazy<MethodInfo> _queryWithCriteriaOfT = new(() =>
        typeof(DocumentStore).RequireMethod(nameof(queryWithCriteriaOfTAsync), BindingFlags.NonPublic | BindingFlags.Instance));

    /// <summary>
    /// jasperfx#869: a diagnostic document page with <see cref="DocumentQueryOptions.Where" /> and / or
    /// <see cref="DocumentQueryOptions.OrderBy" /> applied by Marten's own LINQ provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The mechanism: Marten's LINQ decides WHICH rows and in what order; the select list is still the
    /// diagnostics one.</b> The criteria are composed onto <c>session.Query&lt;T&gt;()</c> for the type the
    /// caller named, and the statement Marten parses out of that is executed with its select clause
    /// swapped for <see cref="StoredDocumentReader" />'s column list — the same technique the cursor-paging
    /// path uses to ride extra columns on a LINQ statement. One reason for each half:
    /// </para>
    /// <list type="bullet">
    /// <item>The <em>selection</em> has to be Marten's. Member casing (PascalCase or camelCase), enums
    /// stored as integers or strings, duplicated fields, sub-class narrowing through <c>mt_doc_type</c>,
    /// conjoined tenancy and soft deletes are all things the provider already resolves from the mapping;
    /// re-deriving any of them by hand is how a filter returns zero rows instead of failing. The user's text
    /// never reaches SQL as text — it becomes an expression tree, and the provider parameterizes it.</item>
    /// <item>The <em>rows</em> have to be the diagnostics read. <see cref="StoredDocument" /> is the
    /// byte-exact stored JSON plus the row's metadata columns, which a LINQ projection cannot give: a
    /// document selector re-serializes, and no LINQ member reaches <c>mt_version</c>, <c>tenant_id</c> or
    /// <c>mt_deleted</c>. Swapping only the select list keeps the WHERE, ORDER BY, LIMIT and OFFSET exactly
    /// as Marten wrote them, in one statement, rather than reading ids first and the rows second.</item>
    /// </list>
    /// <para>
    /// The rest of the contract rides on the same session: it is opened with
    /// <see cref="SessionOptions.ForDatabase(string, Marten.Storage.IMartenDatabase)" /> for the database
    /// <c>findDiagnosticsDatabaseAsync</c> already found (so an unknown tenant is never provisioned) and the
    /// normalized tenant, which is what scopes a conjoined read. <c>AllTenants</c> is <c>AnyTenant()</c>,
    /// <c>IncludeSoftDeleted</c> is <c>MaybeDeleted()</c>, and <c>IdEquals</c> and the metadata filters are
    /// <c>MatchesSql</c> fragments over the same converted values the raw read binds. Ordering is
    /// <c>tenant_id</c> first under <c>AllTenants</c>, then the caller's ordering, then <c>d.id</c> — the
    /// identity column rather than the C# id member as the tie-breaker, because the column is unique per
    /// tenant whatever the member is called or typed (a strong-typed id, an <c>[Identity]</c> member).
    /// </para>
    /// <para>
    /// No storage is ensured, unlike an ordinary LINQ query: a diagnostic read must not issue DDL, and the
    /// raw read beside it does not either.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "jasperfx#869: closes queryWithCriteriaAsync<T> over the document type, and DocumentQueryCriteria.ApplyCriteriaTo is RequiresDynamicCode. Both are reached only when DocumentQueryCriteria.IsAvailable, which is false under Native AOT — that case is refused first.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "jasperfx#869: DocumentQueryCriteria.ApplyCriteriaTo resolves members of the document type reflectively. Document types are preserved at the StoreOptions registration boundary per the AOT publishing guide.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "jasperfx#869: MakeGenericMethod over a mapped document type, which is preserved at the StoreOptions registration boundary.")]
    private Task<DocumentQueryResult> queryWithCriteriaAsync(DiagnosticsTarget target, IMartenDatabase database,
        DocumentQueryOptions options, int pageNumber, int pageSize, CancellationToken token)
    {
        if (!DocumentQueryCriteria.IsAvailable)
        {
            // Before the MakeGenericMethod below, not after: ApplyCriteriaTo would refuse too, but only once a
            // generic method had been closed at runtime, which is the thing Native AOT cannot do.
            throw new DocumentCriteriaNotSupportedException(
                string.IsNullOrWhiteSpace(options.Where) ? nameof(DocumentQueryOptions.OrderBy) : nameof(DocumentQueryOptions.Where),
                "property predicates and orderings are not available in a Native AOT process: they are translated with runtime code generation. Page without them, or narrow with IdEquals and the metadata filters.");
        }

        // An async method: everything it throws arrives in the task, never as a TargetInvocationException.
        return (Task<DocumentQueryResult>)_queryWithCriteriaOfT.Value.MakeGenericMethod(target.RequestedType)
            .Invoke(this, [target, database, options, pageNumber, pageSize, token])!;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "jasperfx#869: only reached through queryWithCriteriaAsync, which refuses under Native AOT first.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "jasperfx#869: only reached through queryWithCriteriaAsync, which refuses under Native AOT first.")]
    private async Task<DocumentQueryResult> queryWithCriteriaOfTAsync<T>(DiagnosticsTarget target,
        IMartenDatabase database, DocumentQueryOptions options, int pageNumber, int pageSize,
        CancellationToken token) where T : notnull
    {
        var tenantId = DocumentQueryOptions.NormalizeTenantId(options.TenantId) ?? StorageConstants.DefaultTenantId;

        await using var session = (QuerySession)QuerySession(SessionOptions.ForDatabase(tenantId, database));

        // Query<T> for the type the caller NAMED, so a sub-class name narrows to that sub-class (and its
        // descendants) through the provider's own mt_doc_type filter, and its own members resolve.
        IQueryable<T> queryable = session.Query<T>();

        if (options.IncludeSoftDeleted && target.IsSoftDeleted)
        {
            queryable = queryable.Where(x => x.MaybeDeleted());
        }

        if (options.AllTenants && target.IsConjoined)
        {
            queryable = queryable.Where(x => x.AnyTenant());
        }

        var idMatches = true;
        if (options.IdEquals != null)
        {
            if (DiagnosticsPredicate.TryConvertId(target.Mapping, options.IdEquals, out var id))
            {
                queryable = queryable.Where(x => x.MatchesSql("d.id = ?", id));
            }
            else
            {
                idMatches = false;
            }
        }

        foreach (var (column, value) in DiagnosticsPredicate.MetadataFilters(target.Mapping, options))
        {
            var sql = $"d.{column} = ?";
            queryable = queryable.Where(x => x.MatchesSql(sql, value));
        }

        // Parse failures, allow-list refusals and Marten's own shape refusals throw here, before the database is
        // touched -- and before the unmatchable-id answer below, so a malformed predicate is reported whatever
        // id came with it. Where and OrderBy are applied in two passes only so each gets its own policy: an
        // enum member is fine as an equality operand and refused as an ordering key, and a shape rule sees
        // one node at a time, not which clause it is in.
        var members = ((ILinqDocumentStorage)session.StorageFor(typeof(T))).QueryMembers;
        var policies = DiagnosticsCriteriaPolicies(members, typeof(T));
        var composed = (options with { OrderBy = null }).ApplyCriteriaTo(queryable, policy: policies.Where);
        composed = (options with { Where = null, Arguments = null }).ApplyCriteriaTo(composed, policy: policies.OrderBy);
        composed = DateTimeKindAdapter.Adapt(composed, members);

        if (!idMatches)
        {
            // An id that is not a value of the identity type matches nothing -- the raw read's answer too.
            return emptyPage(pageNumber, pageSize);
        }

        var provider = ((MartenLinqQueryable<T>)composed).MartenProvider;
        var reader = new StoredDocumentReader(target, "d");
        var rows = new List<StoredDocument>();
        long total;

        try
        {
            var countHandler = new LinqQueryParser(provider, session, composed.Expression, SingleValueMode.LongCount)
                .BuildHandler<long>();
            total = await provider.ExecuteHandlerAsync(countHandler, token).ConfigureAwait(false);

            var page = composed.Skip((pageNumber - 1) * pageSize).Take(pageSize);
            var statements = new LinqQueryParser(provider, session, page.Expression).BuildStatements();
            var selector = statements.MainSelector;

            selector.SelectClause = new StoredDocumentSelectClause(selector.SelectClause, reader.SelectFields);

            // Every ordering here is the caller's. PostgreSQL sorts nulls LAST ascending and FIRST descending;
            // the text means what it means in C#, where null is the smallest value. Without this, ordering by a
            // nullable member returns every row in a different order from the oracle -- and pages differently.
            for (var i = 0; i < selector.Ordering.Expressions.Count; i++)
            {
                if (selector.Ordering.Expressions[i] is LiteralOrdering literal)
                {
                    var descending = literal.Sql.EndsWith(" desc", StringComparison.OrdinalIgnoreCase);
                    selector.Ordering.Expressions[i] =
                        new LiteralOrdering(literal.Sql + (descending ? " nulls last" : " nulls first"));
                }
            }

            // Tenant first under AllTenants (jasperfx#928: a page never repeats another tenant's row), then
            // the caller's ordering as Marten translated it, then the identity column so equal keys still page
            // deterministically -- (tenant_id, id) is the conjoined primary key, so the order is total.
            if (options.AllTenants && target.IsConjoined)
            {
                selector.Ordering.Expressions.Insert(0, new LiteralOrdering($"d.{TenantIdColumn.Name}"));
            }

            selector.Ordering.Add("d.id");

            var command = statements.Top.BuildCommand(session);

            await using var dbReader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
            while (await dbReader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(await reader.ReadAsync(dbReader, tenantId, token).ConfigureAwait(false));
            }
        }
        catch (Exception e) when (isCriteriaTranslationFailure(e))
        {
            throw options.Untranslatable(e);
        }

        return new DocumentQueryResult(rows, total, pageNumber, pageSize);
    }

    /// <summary>
    /// Whether an exception thrown while running a criteria-bearing diagnostic query is Marten refusing the
    /// shape, as against the database being unreachable, a timeout or cancellation — which propagate as
    /// themselves.
    /// </summary>
    /// <remarks>
    /// JasperFx's own test plus Marten's <see cref="Marten.Exceptions.BadLinqExpressionException" />, which
    /// stays on Marten's exception hierarchy (marten#5346) rather than deriving from the JasperFx type
    /// <see cref="DocumentQueryCriteria.IsTranslationFailure" /> recognizes.
    /// </remarks>
    private static bool isCriteriaTranslationFailure(Exception e)
        => e is not DocumentCriteriaNotSupportedException
           && (DocumentQueryCriteria.IsTranslationFailure(e)
               || e is Marten.Exceptions.BadLinqExpressionException
               // A DateTime of the wrong Kind for the column it is compared with -- the arm
               // DateTimeKindAdapter does not cover, e.g. an in-process Local argument against a
               // timestamptz duplicated field.
               || e is Marten.Exceptions.InvalidUtcDateTimeUsageException
               || e is Marten.Exceptions.InvalidDateTimeUsageException);

    /// <summary>
    /// jasperfx#869: makes a <see cref="DateTime" /> argument the Kind Marten will bind it as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JasperFx reads every date in criteria text as UTC — a literal, an <c>@n</c> string, and every
    /// argument that crossed a wire as JSON — so the constant compared with a <see cref="DateTime" /> member
    /// is always <c>Kind=Utc</c>. Marten reads a <see cref="DateTime" /> member as
    /// <c>timestamp without time zone</c> (<c>mt_immutable_timestamp</c> over the JSON, or a duplicated column
    /// created that way, which is the default), and Npgsql refuses to bind a UTC value to that type. Without
    /// this, every <see cref="DateTime" /> comparison an operator could type would be refused.
    /// </para>
    /// <para>
    /// The rewrite keeps the wall-clock value and drops the Kind, which compares like with like: a UTC
    /// <see cref="DateTime" /> is serialized with its <c>Z</c>, and the cast to <c>timestamp without time
    /// zone</c> keeps that same wall clock. It is applied only where the member it is compared with resolves
    /// to a <c>timestamp without time zone</c> locator; a duplicated field created as <c>timestamptz</c>
    /// (<c>useTimestampWithoutTimeZoneForDateTime: false</c>) is left alone, because Npgsql wants exactly the
    /// UTC value there.
    /// </para>
    /// </remarks>
    private sealed class DateTimeKindAdapter: ExpressionVisitor
    {
        private readonly IQueryableMemberCollection _members;
        private bool _changed;

        private DateTimeKindAdapter(IQueryableMemberCollection members)
        {
            _members = members;
        }

        public static IQueryable<T> Adapt<T>(IQueryable<T> queryable, IQueryableMemberCollection members) where T : notnull
        {
            var adapter = new DateTimeKindAdapter(members);
            var expression = adapter.Visit(queryable.Expression)!;

            return adapter._changed ? queryable.Provider.CreateQuery<T>(expression) : queryable;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual)
            {
                if (isUnspecifiedMember(node.Left) && utcConstant(node.Right) is { } right)
                {
                    _changed = true;
                    return node.Update(Visit(node.Left)!, node.Conversion, right);
                }

                if (isUnspecifiedMember(node.Right) && utcConstant(node.Left) is { } left)
                {
                    _changed = true;
                    return node.Update(left, node.Conversion, Visit(node.Right)!);
                }
            }

            return base.VisitBinary(node);
        }

        private bool isUnspecifiedMember(Expression expression)
        {
            var member = unconvert(expression);
            if (member is not MemberExpression || (member.Type != typeof(DateTime) && member.Type != typeof(DateTime?)))
            {
                return false;
            }

            try
            {
                return _members.MemberFor(member) is not DuplicatedField { DbType: NpgsqlTypes.NpgsqlDbType.TimestampTz };
            }
            catch (Exception)
            {
                // Not a member Marten can locate. Leave the expression alone; the provider will refuse it.
                return false;
            }
        }

        private static ConstantExpression? utcConstant(Expression expression)
            => unconvert(expression) is ConstantExpression { Value: DateTime { Kind: DateTimeKind.Utc } utc } constant
                ? Expression.Constant(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), expression.Type)
                : null;

        private static Expression unconvert(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
                ? unconvert(convert.Operand)
                : expression;
    }

    /// <summary>
    /// jasperfx#869: replaces a LINQ statement's select list with the diagnostics column list, leaving its
    /// FROM, WHERE, ORDER BY, LIMIT and OFFSET exactly as Marten built them. Rows are read by
    /// <see cref="StoredDocumentReader" />, never by a Marten selector, so the handler members refuse.
    /// </summary>
    internal sealed class StoredDocumentSelectClause: ISelectClause, IModifyableFromObject, ISelectFieldsFragment
    {
        private readonly string[] _fields;

        public StoredDocumentSelectClause(ISelectClause inner, string[] fields)
        {
            _fields = fields;
            FromObject = inner.FromObject;
            SelectedType = inner.SelectedType;
        }

        public Type SelectedType { get; }

        public string FromObject { get; set; }

        public void Apply(ICommandBuilder sql)
        {
            sql.Append("select ");
            ApplySelectFields(sql);
            sql.Append(" from ");
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public void ApplySelectFields(ICommandBuilder sql) => sql.Append(string.Join(", ", _fields));

        public string[] SelectFields() => _fields;

        public ISelector BuildSelector(IStorageSession session)
            => throw new NotSupportedException("Diagnostic document rows are read by StoredDocumentReader.");

        public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment topStatement,
            ISqlFragment currentStatement) where TResult : notnull
            => throw new NotSupportedException("Diagnostic document rows are read by StoredDocumentReader.");

        public ISelectClause UseStatistics(QueryStatistics statistics)
            => throw new NotSupportedException("Diagnostic document queries count with a separate statement.");
    }
}

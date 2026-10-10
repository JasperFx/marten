using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using JasperFx.Linq;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class CriteriaMetaDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class CriteriaTicket
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public int Priority { get; set; }
}

/// <summary>
/// jasperfx#869 — what is Marten's own about diagnostic document criteria. The cross-store semantics are
/// pinned by <c>DocumentStoreDiagnosticsCompliance</c> and the per-shape verdicts by
/// <see cref="document_store_diagnostics_criteria_shape_matrix" />; these are the store-specific seams:
/// the row read staying byte-exact, metadata filters, database-per-tenant, duplicated enum storage, soft
/// deletes, and which failures are refusals versus failures.
/// </summary>
public class document_store_diagnostics_criteria_tests
{
    private static readonly string OrderType = typeof(CriteriaOrder).FullNameInCode();

    private static async Task<DocumentStore> storeFor(string schema, Action<StoreOptions> configure)
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.DisableNpgsqlLogging = true;
            configure(opts);
        });
    }

    [Theory]
    [InlineData(Casing.Default, EnumStorage.AsInteger)]
    [InlineData(Casing.CamelCase, EnumStorage.AsString)]
    public async Task a_criteria_row_is_the_same_stored_json_and_metadata_as_the_raw_read(Casing casing,
        EnumStorage enumStorage)
    {
        // The criteria read swaps only the SELECT list onto Marten's LINQ statement; the row reader is the
        // raw read's. So the same document read both ways must come back identical, field for field —
        // including the JSON text exactly as stored, in whatever casing the serializer wrote.
        await using var store = await storeFor($"diag869_rows_{casing}_{enumStorage}".ToLowerInvariant(), opts =>
        {
            opts.UseSystemTextJsonForSerialization(enumStorage, casing);
            opts.Schema.For<CriteriaOrder>();
        });

        var orders = document_store_diagnostics_criteria_shape_matrix.Seed();
        await using (var session = store.LightweightSession())
        {
            session.Store(orders.ToArray());
            await session.SaveChangesAsync();
        }

        var diagnostics = (IDocumentStoreDiagnostics)store;
        var target = orders.First(x => x.Status == CriteriaOrderStatus.Shipped);

        var viaCriteria = (await diagnostics.QueryDocumentsAsync(OrderType,
            new DocumentQueryOptions(1, 10)
            {
                Where = "Id = @0 and Status = @1", Arguments = [target.Id, "Shipped"]
            })).Documents.ShouldHaveSingleItem();

        var viaRawRead = await diagnostics.LoadDocumentAsync(OrderType, target.Id.ToString(), null);

        viaCriteria.ShouldBe(viaRawRead);

        var json = JsonDocument.Parse(viaCriteria.Json).RootElement;
        var statusKey = casing == Casing.CamelCase ? "status" : "Status";
        if (enumStorage == EnumStorage.AsString)
        {
            json.GetProperty(statusKey).GetString().ShouldBe("Shipped");
        }
        else
        {
            json.GetProperty(statusKey).GetInt32().ShouldBe((int)CriteriaOrderStatus.Shipped);
        }
    }

    [Fact]
    public async Task where_combines_with_the_metadata_filters()
    {
        await using var store = await storeFor("diag869_metadata", opts =>
        {
            opts.Schema.For<CriteriaMetaDoc>().Metadata(m =>
            {
                m.CorrelationId.Enabled = true;
                m.CausationId.Enabled = true;
                m.LastModifiedBy.Enabled = true;
            });
        });

        // doc-0..doc-3 under correlation c0, doc-4..doc-7 under c1; odd indices by "b1".
        for (var i = 0; i < 8; i++)
        {
            await using var session = store.LightweightSession();
            session.CorrelationId = i < 4 ? "c0" : "c1";
            session.CausationId = "u0";
            session.LastModifiedBy = i % 2 == 0 ? "b0" : "b1";
            session.Store(new CriteriaMetaDoc { Id = Guid.NewGuid(), Name = $"doc-{i}" });
            await session.SaveChangesAsync();
        }

        var diagnostics = (IDocumentStoreDiagnostics)store;

        var result = await diagnostics.QueryDocumentsAsync(typeof(CriteriaMetaDoc).FullNameInCode(),
            new DocumentQueryOptions(1, 10)
            {
                CorrelationId = "c1",
                LastModifiedBy = "b1",
                Where = "Name != @0",
                Arguments = ["doc-5"],
                OrderBy = "Name desc"
            });

        result.TotalCount.ShouldBe(1);
        result.Documents.Select(x => JsonDocument.Parse(x.Json).RootElement.GetProperty("Name").GetString())
            .ShouldBe(["doc-7"]);
    }

    [Fact]
    public async Task where_reads_only_the_named_tenants_own_database()
    {
        const string tenantA = "diag869_tenant_a";
        const string tenantB = "diag869_tenant_b";

        await using var store = await storeFor("diag869_per_db", opts =>
        {
            opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString,
                t => t.WithTenants(tenantA, tenantB));
            opts.Schema.For<CriteriaTicket>();
        });

        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        await using (var session = store.LightweightSession(tenantA))
        {
            session.Store(new CriteriaTicket { Id = Guid.NewGuid(), Title = "a-low", Priority = 1 },
                new CriteriaTicket { Id = Guid.NewGuid(), Title = "a-high", Priority = 9 });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession(tenantB))
        {
            session.Store(new CriteriaTicket { Id = Guid.NewGuid(), Title = "b-high", Priority = 8 });
            await session.SaveChangesAsync();
        }

        var diagnostics = (IDocumentStoreDiagnostics)store;
        var type = typeof(CriteriaTicket).FullNameInCode();
        var highPriority = new DocumentQueryOptions(1, 10) { Where = "Priority > @0", Arguments = [5] };

        var a = await diagnostics.QueryDocumentsAsync(type, highPriority with { TenantId = tenantA });
        a.TotalCount.ShouldBe(1);
        JsonDocument.Parse(a.Documents.Single().Json).RootElement.GetProperty("Title").GetString().ShouldBe("a-high");
        a.Documents.Single().TenantId.ShouldBe(tenantA);

        var b = await diagnostics.QueryDocumentsAsync(type, highPriority with { TenantId = tenantB });
        b.TotalCount.ShouldBe(1);
        JsonDocument.Parse(b.Documents.Single().Json).RootElement.GetProperty("Title").GetString().ShouldBe("b-high");

        // AllTenants across several databases is still refused, criteria or not (#5544).
        var refused = await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(() =>
            diagnostics.QueryDocumentsAsync(type, highPriority with { AllTenants = true }));
        refused.Criterion.ShouldBe(nameof(DocumentQueryOptions.AllTenants));
    }

    [Fact]
    public async Task where_with_no_tenant_reads_only_the_default_tenant_of_a_conjoined_type()
    {
        await using var store = await storeFor("diag869_conjoined_default", opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<CriteriaTicket>();
        });

        var mine = new CriteriaTicket { Id = Guid.NewGuid(), Title = "default", Priority = 5 };
        await using (var session = store.LightweightSession())
        {
            session.Store(mine);
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession("other"))
        {
            session.Store(new CriteriaTicket { Id = Guid.NewGuid(), Title = "other", Priority = 5 });
            await session.SaveChangesAsync();
        }

        var result = await ((IDocumentStoreDiagnostics)store).QueryDocumentsAsync(
            typeof(CriteriaTicket).FullNameInCode(),
            new DocumentQueryOptions(1, 10) { Where = "Priority = @0", Arguments = [5] });

        result.Documents.Select(x => Guid.Parse(x.Id)).ShouldBe([mine.Id]);
        result.Documents.Single().TenantId.ShouldBe(StorageConstants.DefaultTenantId);
    }

    [Fact]
    public async Task include_soft_deleted_with_where_returns_the_matching_deleted_row_flagged()
    {
        await using var store = await storeFor("diag869_soft", opts => opts.Schema.For<CriteriaTicket>().SoftDeleted());

        var live = new CriteriaTicket { Id = Guid.NewGuid(), Title = "live", Priority = 5 };
        var deleted = new CriteriaTicket { Id = Guid.NewGuid(), Title = "deleted", Priority = 7 };
        var deletedButLow = new CriteriaTicket { Id = Guid.NewGuid(), Title = "deleted-low", Priority = 1 };
        await using (var session = store.LightweightSession())
        {
            session.Store(live, deleted, deletedButLow);
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Delete(deleted);
            session.Delete(deletedButLow);
            await session.SaveChangesAsync();
        }

        var diagnostics = (IDocumentStoreDiagnostics)store;
        var options = new DocumentQueryOptions(1, 10) { Where = "Priority > @0", Arguments = [2], OrderBy = "Priority" };

        var withoutDeleted = await diagnostics.QueryDocumentsAsync(typeof(CriteriaTicket).FullNameInCode(), options);
        withoutDeleted.Documents.Select(x => Guid.Parse(x.Id)).ShouldBe([live.Id]);

        var withDeleted = await diagnostics.QueryDocumentsAsync(typeof(CriteriaTicket).FullNameInCode(),
            options with { IncludeSoftDeleted = true });
        withDeleted.TotalCount.ShouldBe(2);
        withDeleted.Documents.Select(x => (Guid.Parse(x.Id), x.IsDeleted)).ShouldBe([(live.Id, false), (deleted.Id, true)]);
    }

    [Theory]
    // Serializer stores enums as integers, but the duplicated column is varchar: the column is what the
    // provider compares, so the range comparison is alphabetical and must be refused.
    [InlineData(EnumStorage.AsInteger, EnumStorage.AsString, true)]
    // Serializer stores names, but the duplicated column is an integer: compared by value, so allowed.
    [InlineData(EnumStorage.AsString, EnumStorage.AsInteger, false)]
    public async Task the_enum_range_refusal_follows_the_storage_the_provider_actually_compares(
        EnumStorage serializer, EnumStorage duplicatedColumn, bool refused)
    {
        await using var store = await storeFor($"diag869_enum_{serializer}_{duplicatedColumn}".ToLowerInvariant(), opts =>
        {
            opts.UseSystemTextJsonForSerialization(serializer);
            opts.Advanced.DuplicatedFieldEnumStorage = duplicatedColumn;
            opts.Schema.For<CriteriaOrder>().Duplicate(x => x.Status);
        });

        var orders = document_store_diagnostics_criteria_shape_matrix.Seed();
        await using (var session = store.LightweightSession())
        {
            session.Store(orders.ToArray());
            await session.SaveChangesAsync();
        }

        var diagnostics = (IDocumentStoreDiagnostics)store;
        var where = new DocumentQueryOptions(1, 100) { Where = "Status > @0", Arguments = [0] };
        var orderBy = new DocumentQueryOptions(1, 100) { OrderBy = "Status" };

        if (refused)
        {
            (await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(
                    () => diagnostics.QueryDocumentsAsync(OrderType, where)))
                .Criterion.ShouldBe(nameof(DocumentQueryOptions.Where));

            (await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(
                    () => diagnostics.QueryDocumentsAsync(OrderType, orderBy)))
                .Criterion.ShouldBe(nameof(DocumentQueryOptions.OrderBy));
        }
        else
        {
            var result = await diagnostics.QueryDocumentsAsync(OrderType, where);
            result.Documents.Select(x => Guid.Parse(x.Id)).OrderBy(x => x)
                .ShouldBe(orders.Where(x => x.Status > 0).Select(x => x.Id).OrderBy(x => x));

            var ordered = await diagnostics.QueryDocumentsAsync(OrderType, orderBy);
            ordered.Documents.Select(x => Guid.Parse(x.Id))
                .ShouldBe(orders.OrderBy(x => x.Status).ThenBy(x => x.Id).Select(x => x.Id));
        }

        // Equality is unaffected by the storage either way.
        var shipped = await diagnostics.QueryDocumentsAsync(OrderType,
            new DocumentQueryOptions(1, 100) { Where = "Status = @0", Arguments = ["Shipped"] });
        shipped.TotalCount.ShouldBe(orders.Count(x => x.Status == CriteriaOrderStatus.Shipped));
    }

    [Fact]
    public async Task an_untranslatable_shape_is_a_refusal_carrying_marten_s_own_failure()
    {
        await using var store = await storeFor("diag869_untranslatable", opts => opts.Schema.For<CriteriaOrder>());
        await using (var session = store.LightweightSession())
        {
            session.Store(document_store_diagnostics_criteria_shape_matrix.Seed().ToArray());
            await session.SaveChangesAsync();
        }

        var refused = await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(() =>
            ((IDocumentStoreDiagnostics)store).QueryDocumentsAsync(OrderType,
                new DocumentQueryOptions(1, 10) { Where = "PlacedAt.Year = @0", Arguments = [2025] }));

        refused.Criterion.ShouldBe(nameof(DocumentQueryOptions.Where));
        // Marten's BadLinqExpressionException stays on Marten's hierarchy (marten#5346), so it is the store,
        // not JasperFx's IsTranslationFailure, that has to recognise it.
        refused.InnerException.ShouldBeOfType<Marten.Exceptions.BadLinqExpressionException>();
    }

    [Fact]
    public async Task cancellation_propagates_as_itself_and_is_not_a_refusal()
    {
        await using var store = await storeFor("diag869_cancel", opts => opts.Schema.For<CriteriaOrder>());
        await using (var session = store.LightweightSession())
        {
            session.Store(document_store_diagnostics_criteria_shape_matrix.Seed().ToArray());
            await session.SaveChangesAsync();
        }

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var thrown = await Should.ThrowAsync<Exception>(() =>
            ((IDocumentStoreDiagnostics)store).QueryDocumentsAsync(OrderType,
                new DocumentQueryOptions(1, 10) { Where = "Total > @0", Arguments = [1m] }, cancelled.Token));

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task a_criteria_read_issues_no_ddl()
    {
        // An ordinary LINQ query ensures the document's storage exists first. A diagnostic read must not --
        // the raw read beside it never has -- so a type with no table yet fails as a missing table, and
        // still has no table afterwards. The failure is NOT a criteria refusal: nothing was wrong with the
        // text.
        const string schema = "diag869_no_ddl";
        await using var store = await storeFor(schema, opts => opts.Schema.For<CriteriaTicket>());

        var thrown = await Should.ThrowAsync<Exception>(() =>
            ((IDocumentStoreDiagnostics)store).QueryDocumentsAsync(typeof(CriteriaTicket).FullNameInCode(),
                new DocumentQueryOptions(1, 10) { Where = "Priority > @0", Arguments = [1] }));
        thrown.ShouldNotBeOfType<DocumentCriteriaNotSupportedException>();
        thrown.ToString().ShouldContain("42P01"); // undefined_table -- and nothing else went wrong first

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync(schemas: [schema]);
        tables.ShouldBeEmpty();
    }

    [Fact]
    public void the_where_and_order_policies_carry_the_string_enum_rules()
    {
        // The rules are member-aware (they resolve through Marten's query members), so they are built per
        // document type; this pins that both policies carry one, on top of JasperFx's defaults.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);
            opts.Schema.For<CriteriaOrder>();
        });

        var mapping = (Marten.Schema.DocumentMapping)store.Options.Storage.FindMapping(typeof(CriteriaOrder));
        var (where, orderBy) = store.DiagnosticsCriteriaPolicies(mapping.QueryMembers, typeof(CriteriaOrder));

        where.ShapeRules.Count.ShouldBe(DynamicQueryPolicy.Default.ShapeRules.Count + 1);
        orderBy.ShapeRules.Count.ShouldBe(DynamicQueryPolicy.Default.ShapeRules.Count + 1);

        Should.Throw<DynamicQueryException>(() => DynamicQuery.Validate(typeof(CriteriaOrder),
            new DynamicQueryText("Status > 1"), where));
        Should.Throw<DynamicQueryException>(() => DynamicQuery.Validate(typeof(CriteriaOrder),
            new DynamicQueryText(null, "Status"), orderBy));

        // Equality is untouched, and the Where rule does not fire on an ordering it never sees.
        DynamicQuery.Validate(typeof(CriteriaOrder), new DynamicQueryText("Status = \"Open\""), where);
        DynamicQuery.Validate(typeof(CriteriaOrder), new DynamicQueryText(null, "Total"), orderBy);
    }
}

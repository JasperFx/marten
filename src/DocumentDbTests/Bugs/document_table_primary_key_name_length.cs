using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

/// <summary>
/// Follow-up to #5271, which fixed this for the natural key table. A document table's primary key
/// constraint was left on Weasel's <c>pkey_{table}_{columns}</c> default, and that runs past
/// PostgreSQL's 63 character limit for a document type name of no great length once <c>tenant_id</c>
/// joins the key under conjoined tenancy.
/// <para>
/// PostgreSQL truncates such a name rather than rejecting it, and the constraint's backing index is a
/// schema-scoped object — so two document types whose names agree for long enough do not merely share
/// a confusing constraint name, they collide outright on the second <c>CREATE TABLE</c>.
/// </para>
/// </summary>
// Sized deliberately: each table name is exactly 63 characters, which PostgreSQL accepts, while the
// derived pkey_{table}_id runs to 71 and truncates to a name the two share. Top level rather than
// nested so the table name is mt_doc_{type} and the arithmetic is legible. Nested aggregate types
// under conjoined tenancy reach these lengths without contrivance.
public class LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class LongDocumentTypeNameForPrimaryKeyConstraintTruncatiBetaa
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class document_table_primary_key_name_length: OneOffConfigurationsContext
{

    [Fact]
    public void the_primary_key_constraint_name_fits_within_the_identifier_limit()
    {
        StoreOptions(opts => opts.Schema.For<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha>());

        var mapping = theStore.Options.Storage
            .MappingFor(typeof(LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha));
        var table = new DocumentTable(mapping);

        // The untruncated form is what the default would have produced.
        $"pkey_{table.Identifier.Name}_{string.Join("_", table.PrimaryKeyColumns)}"
            .Length.ShouldBeGreaterThan(PostgresqlIdentifier.DefaultMaxLength);

        table.PrimaryKeyName.Length.ShouldBeLessThanOrEqualTo(PostgresqlIdentifier.DefaultMaxLength);
    }

    [Fact]
    public void two_long_document_type_names_get_distinct_primary_key_constraint_names()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha>();
            opts.Schema.For<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiBetaa>();
        });

        var alpha = new DocumentTable(theStore.Options.Storage
            .MappingFor(typeof(LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha)));
        var beta = new DocumentTable(theStore.Options.Storage
            .MappingFor(typeof(LongDocumentTypeNameForPrimaryKeyConstraintTruncatiBetaa)));

        // The deterministic hash suffix is the whole point — a plain truncation would leave these
        // equal, and the second CREATE TABLE would fail with "relation ... already exists".
        alpha.PrimaryKeyName.ShouldNotBe(beta.PrimaryKeyName);
    }

    [Fact]
    public async Task both_tables_can_actually_be_created_in_one_schema()
    {
        // The assertion that matters. Before the fix this threw
        // 42P07 relation "pkey_..." already exists.
        StoreOptions(opts =>
        {
            opts.Schema.For<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha>();
            opts.Schema.For<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiBetaa>();
        });

        await Should.NotThrowAsync(() => theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        await using var session = theStore.LightweightSession();
        session.Store(new LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha { Id = Guid.NewGuid(), Name = "a" });
        session.Store(new LongDocumentTypeNameForPrimaryKeyConstraintTruncatiBetaa { Id = Guid.NewGuid(), Name = "b" });
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        (await query.Query<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiAlpha>().CountAsync()).ShouldBe(1);
        (await query.Query<LongDocumentTypeNameForPrimaryKeyConstraintTruncatiBetaa>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public void an_ordinary_document_type_keeps_the_name_it_always_had()
    {
        // Shorten is a no-op at or below the limit, so nothing changes for a schema that was never
        // being truncated — which is what keeps this from being a migration for everybody.
        StoreOptions(opts => opts.Schema.For<Target>());

        var table = new DocumentTable(theStore.Options.Storage.MappingFor(typeof(Target)));

        table.PrimaryKeyName
            .ShouldBe($"pkey_{table.Identifier.Name}_{string.Join("_", table.PrimaryKeyColumns)}");
    }
}

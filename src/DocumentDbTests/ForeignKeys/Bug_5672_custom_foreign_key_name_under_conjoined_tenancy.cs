using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.ForeignKeys;

/// <summary>
///     #5672. Under conjoined tenancy, <c>DocumentTable</c> moves <c>tenant_id</c> to the front of every
///     document foreign key and regenerated the key's name while doing it. That name can run past
///     PostgreSQL's 63 character limit for type and member names of no great length, and Marten then refuses
///     to start with an exception telling you to override the name -- which the regeneration then threw away.
///     The escape hatch the error message names has to work.
/// </summary>
public class Bug_5672_custom_foreign_key_name_under_conjoined_tenancy: OneOffConfigurationsContext
{
    private DocumentTable tableFor<T>(DocumentStore store) =>
        new((DocumentMapping)store.Options.Storage.MappingFor(typeof(T)));

    private ForeignKey theForeignKey<T>(DocumentStore store) =>
        tableFor<T>(store).ForeignKeys.Single();

    [Fact]
    public void a_custom_name_survives_the_tenant_id_reordering()
    {
        var store = StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<VeryLongDocumentNameDetails>()
                .ForeignKey<VeryLongReferenceDocument>(x => x.VeryLongReferenceDocumentId,
                    fk => fk.Name = "mt_doc_details_reference_id_fkey");
        });

        var fk = theForeignKey<VeryLongDocumentNameDetails>(store);

        fk.Name.ShouldBe("mt_doc_details_reference_id_fkey");

        // the reordering itself still has to happen -- it is why the name was being rewritten
        fk.ColumnNames[0].ShouldBe("tenant_id");
        fk.LinkedNames[0].ShouldBe("tenant_id");
    }

    [Fact]
    public async Task the_schema_applies_at_the_real_identifier_limit_with_a_custom_name()
    {
        // NameDataLength is pinned to PostgreSQL's own default here. The test harness raises it to 100,
        // which is exactly why this never showed up in the suite: the generated 81 character name fit.
        var store = StoreOptions(opts =>
        {
            opts.NameDataLength = 64;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<VeryLongDocumentNameDetails>()
                .ForeignKey<VeryLongReferenceDocument>(x => x.VeryLongReferenceDocumentId,
                    fk => fk.Name = "mt_doc_details_reference_id_fkey");
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task the_schema_applies_at_the_real_identifier_limit_with_the_generated_name()
    {
        // And without a custom name at all: shortening the generated name means the escape hatch is no
        // longer the only way through.
        var store = StoreOptions(opts =>
        {
            opts.NameDataLength = 64;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<VeryLongDocumentNameDetails>()
                .ForeignKey<VeryLongReferenceDocument>(x => x.VeryLongReferenceDocumentId);
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public void the_generated_name_is_still_regenerated_with_tenant_id_first()
    {
        // The reordering exists so the key's name reflects its column order; nothing about #5672 changes
        // that for a name Marten generated itself.
        var store = StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<ShortDetails>().ForeignKey<ShortReference>(x => x.ShortReferenceId);
        });

        var fk = theForeignKey<ShortDetails>(store);

        fk.Name.ShouldBe("mt_doc_shortdetails_tenant_id_short_reference_id_fkey");
        fk.ColumnNames[0].ShouldBe("tenant_id");
    }

    [Fact]
    public void a_generated_name_over_the_identifier_limit_is_shortened_rather_than_refused()
    {
        // Same treatment the document table's own primary key name got in #5271/#4679: a deterministic hash
        // suffix, so two long type names that agree for the first 58 characters still differ.
        var store = StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Schema.For<VeryLongDocumentNameDetails>()
                .ForeignKey<VeryLongReferenceDocument>(x => x.VeryLongReferenceDocumentId);
        });

        var fk = theForeignKey<VeryLongDocumentNameDetails>(store);

        fk.Name.Length.ShouldBeLessThanOrEqualTo(PostgresqlIdentifier.DefaultMaxLength);
        fk.Name.ShouldBe(PostgresqlIdentifier.Shorten(
            "mt_doc_verylongdocumentnamedetails_tenant_id_very_long_reference_document_id_fkey"));
    }

    [Fact]
    public void single_tenancy_is_untouched()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<ShortDetails>().ForeignKey<ShortReference>(x => x.ShortReferenceId,
                fk => fk.Name = "my_own_name_fkey");
        });

        var fk = theForeignKey<ShortDetails>(store);

        fk.Name.ShouldBe("my_own_name_fkey");
        fk.ColumnNames.ShouldBe(["short_reference_id"]);
    }
}

public class VeryLongReferenceDocument
{
    public Guid Id { get; set; }
}

public class VeryLongDocumentNameDetails
{
    public Guid Id { get; set; }
    public Guid VeryLongReferenceDocumentId { get; set; }
}

public class ShortReference
{
    public Guid Id { get; set; }
}

public class ShortDetails
{
    public Guid Id { get; set; }
    public Guid ShortReferenceId { get; set; }
}

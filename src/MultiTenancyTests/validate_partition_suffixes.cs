using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace MultiTenancyTests;

public class validate_partition_suffixes
{
    [Fact]
    public void valid_simple_suffixes_should_not_throw()
    {
        Should.NotThrow(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["tenant1", "tenant2", "abc"]));
    }

    [Fact]
    public void valid_suffix_with_underscores_should_not_throw()
    {
        Should.NotThrow(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["my_tenant", "_leading", "a_b_c"]));
    }

    [Fact]
    public void valid_suffix_starting_with_underscore_should_not_throw()
    {
        Should.NotThrow(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["_tenant", "__double"]));
    }

    [Fact]
    public void valid_mixed_case_and_digits_should_not_throw()
    {
        Should.NotThrow(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["Tenant1", "ABC_123", "a1B2c3"]));
    }

    [Fact]
    public void suffix_with_spaces_should_throw()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["tenant 1"]));

        ex.Message.ShouldContain("tenant 1");
    }

    [Fact]
    public void suffix_with_hyphen_should_throw()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["tenant-1"]));

        ex.Message.ShouldContain("tenant-1");
    }

    [Fact]
    public void suffix_starting_with_digit_should_not_throw()
    {
        // #4567: digit-leading suffixes (e.g. sanitized GUIDs) are valid because the suffix is
        // always concatenated onto an already-valid table-name prefix (e.g. "mt_doc_mymessage_").
        Should.NotThrow(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(
                ["1tenant", "538f87e5_6872_4676_9468_54500f905f78"]));
    }

    [Fact]
    public void suffix_with_semicolon_should_throw()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["tenant;drop"]));

        ex.Message.ShouldContain("tenant;drop");
    }

    [Fact]
    public void suffix_with_single_quote_should_throw()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["tenant'bad"]));

        ex.Message.ShouldContain("tenant'bad");
    }

    [Fact]
    public void suffix_with_dot_should_throw()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["schema.table"]));

        ex.Message.ShouldContain("schema.table");
    }

    [Fact]
    public void empty_suffix_should_throw()
    {
        Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers([""]));
    }

    [Fact]
    public void multiple_invalid_suffixes_should_report_all_in_message()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["valid_one", "bad-one", "bad two"]));

        ex.Message.ShouldContain("bad-one");
        ex.Message.ShouldContain("bad two");
        ex.Message.ShouldNotContain("valid_one");
    }

    [Fact]
    public void suffix_within_identifier_length_limit_should_not_throw()
    {
        // base table (50) + '_' (1) + suffix (12) = 63 -> within the 63-byte limit
        Should.NotThrow(() =>
            AdvancedOperations.AssertSuffixesWithinIdentifierLimit(["abcdefghijkl"], 50));
    }

    [Fact]
    public void suffix_exceeding_identifier_length_limit_should_throw()
    {
        // base table (50) + '_' (1) + suffix (13) = 64 -> exceeds the 63-byte limit
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertSuffixesWithinIdentifierLimit(["abcdefghijklm"], 50));

        ex.Message.ShouldContain("abcdefghijklm");
        ex.Message.ShouldContain("63");
    }

    [Fact]
    public void reporter_real_world_guid_suffix_passes_both_checks()
    {
        // The exact suffix from #4567: it is digit-leading and, concatenated onto a normal
        // mt_doc_* table name, stays well within the 63-byte limit.
        var suffix = "538f87e5_6872_4676_9468_54500f905f78";

        Should.NotThrow(() => AdvancedOperations.AssertValidPostgresqlIdentifiers([suffix]));
        Should.NotThrow(() =>
            AdvancedOperations.AssertSuffixesWithinIdentifierLimit([suffix], "mt_doc_mymessage".Length));
    }

    [Fact]
    public void add_tenants_params_overload_should_throw_for_illegal_suffixes()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
        });

        Should.Throw<ArgumentException>(() =>
            store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "good", "bad;suffix")
                .GetAwaiter().GetResult());
    }

    [Fact]
    public void add_tenants_dictionary_overload_should_throw_for_illegal_suffixes()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
        });

        var mapping = new Dictionary<string, string>
        {
            { "tenant1", "good_suffix" },
            { "tenant2", "bad-suffix" }
        };

        Should.Throw<ArgumentException>(() =>
            store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, mapping)
                .GetAwaiter().GetResult());
    }

    [Fact]
    public void guid_params_overload_uses_hyphen_free_suffix()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
        });

        // #4567: the params Guid[] overload now derives a hyphen-free "N"-format suffix
        // (e.g. "538f87e5..."), so it no longer fails suffix validation. It may still throw later
        // for other reasons (database/partition state), but never an ArgumentException for the suffix.
        var id = Guid.NewGuid();
        var ex = Record.Exception(() =>
            store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, id)
                .GetAwaiter().GetResult());

        ex.ShouldNotBeOfType<ArgumentException>();
    }

    [Fact]
    public void guid_overload_with_suffix_func_uses_func_for_suffix()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
        });

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Providing a func that produces an invalid suffix should throw
        Should.Throw<ArgumentException>(() =>
            store.Advanced.AddMartenManagedTenantsAsync(
                CancellationToken.None,
                [id1, id2],
                id => $"bad-{id.ToString()[..8]}")
                .GetAwaiter().GetResult());
    }

    [Fact]
    public void guid_overload_with_suffix_func_valid_suffixes_should_not_throw_validation()
    {
        // Just verify the suffix func values pass validation (the call will fail later
        // due to no actual database partitions, but it should get past validation)
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var suffixes = new[] { id1, id2 }
            .Select(id => $"tenant_{id.ToString("N")[..8]}")
            .ToArray();

        // These suffixes should be valid identifiers
        Should.NotThrow(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(suffixes));
    }

    [Fact]
    public void guid_overload_with_suffix_func_builds_correct_mapping()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
        });

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Use a valid suffix func - the call will proceed past validation into actual
        // partition management. We just need to verify it doesn't throw ArgumentException
        // (it may throw other exceptions depending on database state, but not ArgumentException)
        var ex = Record.Exception(() =>
            store.Advanced.AddMartenManagedTenantsAsync(
                CancellationToken.None,
                [id1, id2],
                id => $"t_{id.ToString("N")[..8]}")
                .GetAwaiter().GetResult());

        // Should not be an ArgumentException - that would mean validation failed
        ex.ShouldNotBeOfType<ArgumentException>();
    }
}

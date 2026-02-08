using System;
using System.Collections.Generic;
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
    public void suffix_starting_with_digit_should_throw()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            AdvancedOperations.AssertValidPostgresqlIdentifiers(["1tenant"]));

        ex.Message.ShouldContain("1tenant");
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
}

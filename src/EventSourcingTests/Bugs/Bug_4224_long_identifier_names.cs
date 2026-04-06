using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Regression test for issue #4224: auto-discovered tag types with long names
/// generate FK/PK names exceeding PostgreSQL's 63-char NAMEDATALEN limit.
/// </summary>
public class Bug_4224_long_identifier_names : OneOffConfigurationsContext
{
    // This long type name triggers the issue when used as a tag type
    public record struct BootstrapTokenResourceName(string Value);
    public record BootstrapTokenResourceNameCreated(BootstrapTokenResourceName ResourceName);

    public class LongNameAggregate
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";

        public void Apply(BootstrapTokenResourceNameCreated e)
        {
            Name = e.ResourceName.Value;
        }
    }

    [Fact]
    public async Task can_create_schema_with_long_tag_type_name()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.AddEventType<BootstrapTokenResourceNameCreated>();

            // Register the long-named tag type — this would previously fail
            // with PostgresqlIdentifierTooLongException
            opts.Events.RegisterTagType<BootstrapTokenResourceName>("bootstrap_token_resource_name");
        });

        // This should NOT throw PostgresqlIdentifierTooLongException
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public async Task can_create_schema_with_long_tag_type_name_without_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<BootstrapTokenResourceNameCreated>();
            opts.Events.RegisterTagType<BootstrapTokenResourceName>("bootstrap_token_resource_name");
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public void identifier_shortening_is_deterministic()
    {
        var name1 = PostgresqlIdentifier.Shorten(
            "fkey_mt_event_tag_bootstrap_token_resource_name_seq_id_is_archived");
        var name2 = PostgresqlIdentifier.Shorten(
            "fkey_mt_event_tag_bootstrap_token_resource_name_seq_id_is_archived");

        name1.ShouldBe(name2);
        name1.Length.ShouldBeLessThanOrEqualTo(63);
    }

    [Fact]
    public void short_names_are_not_modified()
    {
        var shortName = "fk_mt_events_stream_id";
        PostgresqlIdentifier.Shorten(shortName).ShouldBe(shortName);
    }

    [Fact]
    public void different_long_names_produce_different_short_names()
    {
        var name1 = PostgresqlIdentifier.Shorten(
            "fkey_mt_event_tag_bootstrap_token_resource_name_seq_id_is_archived");
        var name2 = PostgresqlIdentifier.Shorten(
            "fkey_mt_event_tag_another_very_long_type_name_here_seq_id_is_archived");

        name1.ShouldNotBe(name2);
        name1.Length.ShouldBeLessThanOrEqualTo(63);
        name2.Length.ShouldBeLessThanOrEqualTo(63);
    }
}

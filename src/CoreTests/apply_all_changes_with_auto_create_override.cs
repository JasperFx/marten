using System.Threading.Tasks;
using JasperFx;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Xunit;

namespace CoreTests;

/// <summary>
///     <c>IMartenStorage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate? override)</c> documents its
///     parameter as "If supplied, this overrides the AutoCreate threshold of this database", but
///     <c>DocumentStore</c> never passed it on: every database was migrated under its own
///     <c>AutoCreateSchemaObjects</c>, whatever the caller asked for. Every in-repo caller that passes a
///     mode passes the one its store already has, which is why nothing noticed.
/// </summary>
/// <remarks>
///     The fixture for both facts is a document table created by an earlier configuration, before a
///     duplicated field was added, so bringing it up to date takes an <c>alter table … add column</c>: an
///     update, which <c>CreateOrUpdate</c> applies and <c>CreateOnly</c> refuses.
/// </remarks>
public class apply_all_changes_with_auto_create_override: OneOffConfigurationsContext
{
    [Fact]
    public async Task a_create_or_update_override_migrates_a_create_only_store()
    {
        await createTargetTableWithoutDuplicatedFieldAsync();

        var store = SeparateStore(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
            opts.Schema.For<Target>().Duplicate(x => x.String);
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);

        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_create_only_override_is_not_overruled_by_a_create_or_update_store()
    {
        await createTargetTableWithoutDuplicatedFieldAsync();

        var store = SeparateStore(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Schema.For<Target>().Duplicate(x => x.String);
        });

        await Should.ThrowAsync<SchemaMigrationException>(async () =>
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOnly);
        });

        // Nothing was altered: the duplicated column is still missing.
        await Should.ThrowAsync<DatabaseValidationException>(async () =>
        {
            await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync(TestContext.Current.CancellationToken);
        });
    }

    private async Task createTargetTableWithoutDuplicatedFieldAsync()
    {
        StoreOptions(opts => opts.Schema.For<Target>());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}

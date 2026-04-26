using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Projections.Flattened;

/// <summary>
/// Regression coverage for:
///  - https://github.com/JasperFx/marten/issues/4291
///    FlatTableProjection mapping enums as strings throws.
///  - https://github.com/JasperFx/marten/issues/4290
///    FlatTableProjection throws when mapping a value object or nullable value
///    object.
///
/// FlatTableProjection now honors StoreOptions.Advanced.DuplicatedFieldEnumStorage
/// for enum columns, supports nullable enums, and auto-unwraps registered
/// value types (and nullable value types) to their inner primitive.
/// </summary>
public class Bug_4290_4291_flat_table_enum_and_value_types : OneOffConfigurationsContext
{
    [Fact]
    public async Task enum_stored_as_string_when_DuplicatedFieldEnumStorage_is_AsString()
    {
        StoreOptions(opts =>
        {
            opts.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;
            opts.Projections.Add<Bug4290StatusProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290StatusChanged(Bug4290Status.Active, null));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var statusValue = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select status from {SchemaName}.bug_4290_status where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        statusValue.ShouldBe(Bug4290Status.Active.ToString());
    }

    [Fact]
    public async Task enum_stored_as_int_when_DuplicatedFieldEnumStorage_is_AsInteger()
    {
        StoreOptions(opts =>
        {
            opts.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsInteger;
            opts.Projections.Add<Bug4290IntStatusProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290StatusChanged(Bug4290Status.Suspended, null));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var statusValue = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select status from {SchemaName}.bug_4290_int_status where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        statusValue.ShouldBe((int)Bug4290Status.Suspended);
    }

    [Fact]
    public async Task nullable_enum_stored_as_string_with_value_present()
    {
        StoreOptions(opts =>
        {
            opts.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;
            opts.Projections.Add<Bug4290StatusProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290StatusChanged(Bug4290Status.Active, Bug4290Status.Suspended));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var previous = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select previous_status from {SchemaName}.bug_4290_status where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        previous.ShouldBe(Bug4290Status.Suspended.ToString());
    }

    [Fact]
    public async Task nullable_enum_stored_as_dbnull_when_value_is_null()
    {
        StoreOptions(opts =>
        {
            opts.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;
            opts.Projections.Add<Bug4290StatusProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290StatusChanged(Bug4290Status.Active, null));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var previous = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select previous_status from {SchemaName}.bug_4290_status where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        previous.ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task value_type_property_is_auto_unwrapped_to_inner_primitive()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType(typeof(Bug4290LegacyId));
            opts.Projections.Add<Bug4290LegacyProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290LegacyAssigned(new Bug4290LegacyId(42), null));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var primaryValue = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select primary_id from {SchemaName}.bug_4290_legacy where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        primaryValue.ShouldBe(42);
    }

    [Fact]
    public async Task nullable_value_type_with_value_unwraps_to_inner_primitive()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType(typeof(Bug4290LegacyId));
            opts.Projections.Add<Bug4290LegacyProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290LegacyAssigned(new Bug4290LegacyId(7), new Bug4290LegacyId(99)));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var secondaryValue = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select secondary_id from {SchemaName}.bug_4290_legacy where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        secondaryValue.ShouldBe(99);
    }

    [Fact]
    public async Task nullable_value_type_with_null_writes_dbnull()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType(typeof(Bug4290LegacyId));
            opts.Projections.Add<Bug4290LegacyProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new Bug4290LegacyAssigned(new Bug4290LegacyId(7), null));
        await theSession.SaveChangesAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var secondaryValue = await Weasel.Core.CommandExtensions
            .CreateCommand(conn,
                $"select secondary_id from {SchemaName}.bug_4290_legacy where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        secondaryValue.ShouldBe(DBNull.Value);
    }
}

// ─────────────────────────── fixtures ───────────────────────────

public enum Bug4290Status
{
    Active,
    Suspended,
    Closed
}

public record Bug4290StatusChanged(Bug4290Status Status, Bug4290Status? PreviousStatus);

// Mirrors the Vogen-style "single-value wrapper struct" pattern from #4290 with
// a single public Value property over an int. Registered with Marten via
// opts.RegisterValueType(typeof(Bug4290LegacyId)).
public readonly struct Bug4290LegacyId
{
    public Bug4290LegacyId(int value) => Value = value;
    public int Value { get; }
}

public record Bug4290LegacyAssigned(Bug4290LegacyId PrimaryId, Bug4290LegacyId? SecondaryId);

// Status column is text — should accept enum-as-string when configured so
public class Bug4290StatusProjection : FlatTableProjection
{
    public Bug4290StatusProjection() : base("bug_4290_status", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<string>("status");
        Table.AddColumn<string>("previous_status").AllowNulls();

        Project<Bug4290StatusChanged>(map =>
        {
            map.Map(x => x.Status);
            map.Map(x => x.PreviousStatus);
        });
    }
}

// Status column is int — should accept enum-as-int when configured so
public class Bug4290IntStatusProjection : FlatTableProjection
{
    public Bug4290IntStatusProjection() : base("bug_4290_int_status", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<int>("status");

        Project<Bug4290StatusChanged>(map =>
        {
            map.Map(x => x.Status);
        });
    }
}

public class Bug4290LegacyProjection : FlatTableProjection
{
    public Bug4290LegacyProjection() : base("bug_4290_legacy", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<int>("primary_id");
        Table.AddColumn<int>("secondary_id").AllowNulls();

        Project<Bug4290LegacyAssigned>(map =>
        {
            map.Map(x => x.PrimaryId);
            map.Map(x => x.SecondaryId);
        });
    }
}

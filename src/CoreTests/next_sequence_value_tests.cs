using System.Threading.Tasks;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class next_sequence_value_tests : OneOffConfigurationsContext
{
    private async Task ensureSequenceAsync(string sequenceName, long startWith = 1)
    {
        // Touch theStore first so OneOffConfigurationsContext's lazy "drop schema"
        // happens before we create our sequence, not after.
        _ = theStore;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.RunSqlAsync(
            $"CREATE SCHEMA IF NOT EXISTS {SchemaName}",
            $"DROP SEQUENCE IF EXISTS {SchemaName}.{sequenceName}",
            $"CREATE SEQUENCE {SchemaName}.{sequenceName} START {startWith}");
    }

    [Fact]
    public async Task next_sequence_value_returns_sequential_ints_with_qualified_string_name()
    {
        await ensureSequenceAsync("seq_int_str");

        #region sample_next_sequence_value_by_string

        await using var session = theStore.QuerySession();

        // Fetch the next value of a PostgreSQL sequence by name.
        // The name can be schema-qualified (e.g. "my_schema.my_sequence").
        var first = await session.NextSequenceValue($"{SchemaName}.seq_int_str");
        var second = await session.NextSequenceValue($"{SchemaName}.seq_int_str");

        #endregion

        var third = await session.NextSequenceValue($"{SchemaName}.seq_int_str");

        first.ShouldBe(1);
        second.ShouldBe(2);
        third.ShouldBe(3);
    }

    [Fact]
    public async Task next_sequence_value_returns_sequential_ints_with_dbobjectname()
    {
        await ensureSequenceAsync("seq_int_obj");

        #region sample_next_sequence_value_by_dbobjectname

        await using var session = theStore.QuerySession();

        // Pass a DbObjectName (here, Weasel's PostgresqlObjectName) when you already have
        // a strongly-typed reference to the sequence — for example, from a Weasel Sequence
        // schema object built by your own FeatureSchemaBase.
        var name = new PostgresqlObjectName(SchemaName, "seq_int_obj");
        var first = await session.NextSequenceValue(name);
        var second = await session.NextSequenceValue(name);

        #endregion

        first.ShouldBe(1);
        second.ShouldBe(2);
    }

    [Fact]
    public async Task next_sequence_value_as_long_returns_sequential_longs_with_qualified_string_name()
    {
        await ensureSequenceAsync("seq_long_str");

        await using var session = theStore.QuerySession();
        var first = await session.NextSequenceValueAsLong($"{SchemaName}.seq_long_str");
        var second = await session.NextSequenceValueAsLong($"{SchemaName}.seq_long_str");

        first.ShouldBe(1L);
        second.ShouldBe(2L);
    }

    [Fact]
    public async Task next_sequence_value_as_long_returns_sequential_longs_with_dbobjectname()
    {
        await ensureSequenceAsync("seq_long_obj");

        await using var session = theStore.QuerySession();
        var name = new PostgresqlObjectName(SchemaName, "seq_long_obj");

        var first = await session.NextSequenceValueAsLong(name);
        var second = await session.NextSequenceValueAsLong(name);

        first.ShouldBe(1L);
        second.ShouldBe(2L);
    }

    [Fact]
    public async Task next_sequence_value_as_long_handles_values_above_int32_max()
    {
        // Sequences can exceed Int32.MaxValue (2,147,483,647) — long overload must handle.
        const long startAboveInt32Max = 3_000_000_000L;
        await ensureSequenceAsync("seq_big", startAboveInt32Max);

        #region sample_next_sequence_value_as_long

        await using var session = theStore.QuerySession();

        // Use NextSequenceValueAsLong when the sequence may exceed Int32.MaxValue
        // (roughly 2.1 billion). nextval() is a bigint in Postgres natively.
        var first = await session.NextSequenceValueAsLong($"{SchemaName}.seq_big");
        var second = await session.NextSequenceValueAsLong(new PostgresqlObjectName(SchemaName, "seq_big"));

        #endregion

        first.ShouldBe(startAboveInt32Max);
        second.ShouldBe(startAboveInt32Max + 1);
    }

    [Fact]
    public async Task next_sequence_value_honors_start_value()
    {
        await ensureSequenceAsync("seq_start", 10_000);

        await using var session = theStore.QuerySession();
        var first = await session.NextSequenceValue(new PostgresqlObjectName(SchemaName, "seq_start"));
        first.ShouldBe(10_000);

        var second = await session.NextSequenceValue($"{SchemaName}.seq_start");
        second.ShouldBe(10_001);
    }
}

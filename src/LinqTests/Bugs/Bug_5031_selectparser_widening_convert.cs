using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

public class Bug_5031_selectparser_widening_convert: BugIntegrationContext
{
    [Fact]
    public async Task widening_convert_still_uses_jsonb_build_object_and_streams()
    {
        theSession.Store(new Target5031 { Id = Guid.NewGuid(), IntValue = 5 });
        await theSession.SaveChangesAsync();

        // Compiler inserts Convert(x.IntValue, long) and Convert(x.IntValue, decimal).
        var queryable = theSession.Query<Target5031>()
            .Select(x => new WidenDto5031 { Long = x.IntValue, Dec = x.IntValue });

        var command = queryable.ToCommand();
        command.CommandText.ShouldContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.ShouldContain(x => x.Long == 5L && x.Dec == 5m);

        // The whole point of #5031: a widening-Convert projection must still be streamable.
        var stream = new MemoryStream();
        await queryable.StreamJsonArray(stream, default);
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().ShouldBe(1);

        // Both widened members stream the source int (5) as a JSON number, regardless of the
        // serializer's property casing.
        var values = doc.RootElement[0].EnumerateObject().Select(p => p.Value.GetInt64()).ToArray();
        values.ShouldBe(new[] { 5L, 5L });
    }

    [Fact]
    public async Task boxing_convert_to_object_still_uses_jsonb_build_object()
    {
        theSession.Store(new Target5031 { Id = Guid.NewGuid(), IntValue = 7 });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5031>()
            .Select(x => new { Boxed = (object)x.IntValue });

        var command = queryable.ToCommand();
        command.CommandText.ShouldContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.Count.ShouldBe(1);
        results[0].Boxed.ShouldNotBeNull();
    }

    [Fact]
    public async Task nullable_wrapping_convert_still_uses_jsonb_build_object()
    {
        theSession.Store(new Target5031 { Id = Guid.NewGuid(), IntValue = 9 });
        await theSession.SaveChangesAsync();

        // Compiler inserts Convert(x.IntValue, int?).
        var queryable = theSession.Query<Target5031>()
            .Select(x => new NullableDto5031 { NInt = x.IntValue });

        var command = queryable.ToCommand();
        command.CommandText.ShouldContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.ShouldContain(x => x.NInt == 9);
    }

    [Fact]
    public async Task lossy_narrowing_cast_falls_back_to_client_side()
    {
        theSession.Store(new Target5031 { Id = Guid.NewGuid(), IntValue = 1, LongValue = 5_000_000_000L });
        await theSession.SaveChangesAsync();

        // Explicit narrowing cast (int)x.LongValue -> Convert(x.LongValue, int): not value-preserving.
        var queryable = theSession.Query<Target5031>()
            .Select(x => new NarrowDto5031 { Int = (int)x.LongValue });

        var command = queryable.ToCommand();
        command.CommandText.ShouldNotContain("jsonb_build_object");
    }

    [Fact]
    public async Task streaming_a_lossy_narrowing_projection_is_refused()
    {
        theSession.Store(new Target5031 { Id = Guid.NewGuid(), IntValue = 1, LongValue = 5_000_000_000L });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5031>()
            .Select(x => new NarrowDto5031 { Int = (int)x.LongValue });

        var stream = new MemoryStream();
        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            await queryable.StreamJsonArray(stream, default));
    }
}

public class Target5031
{
    public Guid Id { get; set; }
    public int IntValue { get; set; }
    public long LongValue { get; set; }
    public decimal DecimalValue { get; set; }
}

public class WidenDto5031
{
    public long Long { get; set; }
    public decimal Dec { get; set; }
}

public class NullableDto5031
{
    public int? NInt { get; set; }
}

public class NarrowDto5031
{
    public int Int { get; set; }
}

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

public class Bug_5011_jsonb_build_object_select_projection: BugIntegrationContext
{
    [Fact]
    public async Task simple_select_uses_jsonb_build_object_and_can_stream_raw_json()
    {
        theSession.Store(new Target5011
        {
            Id = Guid.NewGuid(), Code = "abc", Name = "Widget"
        });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5011>()
            .Select(x => new Target5011Dto(x.Code, x.Name));

        var command = queryable.ToCommand();
        command.CommandText.ShouldContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.ShouldContain(x => x.Code == "abc" && x.Name == "Widget");

        var stream = new MemoryStream();
        await queryable.StreamJsonArray(stream, default);
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task property_names_follow_the_serializer_naming_policy()
    {
        theSession.Store(new Target5011 { Id = Guid.NewGuid(), Code = "abc", Name = "Widget" });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5011>()
            .Select(x => new Target5011Dto(x.Code, x.Name));

        var stream = new MemoryStream();
        await queryable.StreamJsonArray(stream, default);
        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();

        // Default Marten serializer casing is camelCase
        json.ShouldContain("\"code\"");
        json.ShouldContain("\"name\"");
    }

    [Fact]
    public async Task nested_member_access_projects_through_the_json_path()
    {
        theSession.Store(new Target5011
        {
            Id = Guid.NewGuid(), Code = "abc", Name = "Widget", Client = new Client5011 { Name = "Acme" }
        });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5011>()
            .Select(x => new { ClientName = x.Client!.Name });

        var command = queryable.ToCommand();
        command.CommandText.ShouldContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.ShouldContain(x => x.ClientName == "Acme");
    }

    [Fact]
    public async Task method_call_in_select_falls_back_to_client_side_evaluation()
    {
        theSession.Store(new Target5011 { Id = Guid.NewGuid(), Code = "abc", Name = "Widget" });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5011>()
            .Select(x => new Target5011Dto(x.Code!.ToUpper(), x.Name));

        // Should NOT be translated into jsonb_build_object -- the ToUpper() call can't be
        // expressed in SQL, so Marten must fall back to a client-side transform instead of
        // silently dropping the operation.
        var command = queryable.ToCommand();
        command.CommandText.ShouldNotContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.ShouldContain(x => x.Code == "ABC" && x.Name == "Widget");
    }

    [Fact]
    public async Task streaming_raw_json_is_refused_for_a_client_side_fallback_projection()
    {
        theSession.Store(new Target5011 { Id = Guid.NewGuid(), Code = "abc", Name = "Widget" });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5011>()
            .Select(x => new Target5011Dto(x.Code!.ToUpper(), x.Name));

        var stream = new MemoryStream();
        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            await queryable.StreamJsonArray(stream, default));
    }

    [Fact]
    public async Task distinct_composes_with_the_jsonb_build_object_projection()
    {
        theSession.Store(new Target5011 { Id = Guid.NewGuid(), Code = "abc", Name = "Widget" });
        theSession.Store(new Target5011 { Id = Guid.NewGuid(), Code = "abc", Name = "Widget" });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target5011>()
            .Select(x => new Target5011Dto(x.Code, x.Name))
            .Distinct();

        var command = queryable.ToCommand();
        command.CommandText.ShouldContain("jsonb_build_object");

        var results = await queryable.ToListAsync();
        results.Count.ShouldBe(1);
    }
}

public class Target5011
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public Client5011? Client { get; set; }
}

public class Client5011
{
    public string? Name { get; set; }
}

public record Target5011Dto(string Code, string Name);

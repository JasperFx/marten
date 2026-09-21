#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

public class Bug_5461_absent_key_in_select_projection: BugIntegrationContext
{
    private async Task<Guid> storeDocumentMissingTheNewerProperties()
    {
        var id = Guid.NewGuid();
        theSession.Store(new Provider5461
        {
            Id = id,
            AgbCode = "abc",
            CareTypes = new List<string> { "one" },
            ExecuteWlzCheck = true,
            Rank = 5
        });
        await theSession.SaveChangesAsync();

        // Simulates a document that was written before the property existed
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var command = conn.CreateCommand();
        command.CommandText =
            $"update {SchemaName}.mt_doc_provider5461 set data = data - 'ExecuteWlzCheck' - 'Rank' - 'CareTypes'";
        await command.ExecuteNonQueryAsync();

        return id;
    }

    [Fact]
    public async Task absent_key_does_not_break_a_multi_member_projection()
    {
        await storeDocumentMissingTheNewerProperties();

        var queryable = theSession.Query<Provider5461>()
            .Where(x => x.AgbCode == "abc")
            .Select(x => new { x.CareTypes, x.ExecuteWlzCheck, x.Rank });

        var result = await queryable.FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.ExecuteWlzCheck.ShouldBeFalse();
        result.Rank.ShouldBe(0);
    }

    [Fact]
    public void only_the_scalar_pairs_go_through_jsonb_strip_nulls()
    {
        var sql = theSession.Query<Provider5461>()
            .Select(x => new { x.CareTypes, x.ExecuteWlzCheck, x.Rank })
            .ToCommand().CommandText;

        // The collection member is built as it always was -- stripping nulls out of an object or
        // array value would reach inside it -- and only the two scalars are stripped.
        sql.ShouldContain("jsonb_build_object('CareTypes'");
        sql.ShouldContain("jsonb_strip_nulls(jsonb_build_object('ExecuteWlzCheck'");
    }

    [Fact]
    public async Task streamed_json_omits_the_absent_key()
    {
        await storeDocumentMissingTheNewerProperties();

        var stream = new MemoryStream();
        await theSession.Query<Provider5461>()
            .Where(x => x.AgbCode == "abc")
            .Select(x => new { x.CareTypes, x.ExecuteWlzCheck, x.Rank })
            .StreamJsonArray(stream, default);

        stream.Position = 0;
        var json = await new StreamReader(stream).ReadToEndAsync();

        // The absent scalar keys are absent in the projected JSON too, exactly as they are in the
        // stored document. The collection member is deliberately left alone (see above).
        json.ShouldNotContain("ExecuteWlzCheck");
        json.ShouldNotContain("Rank");
    }

    [Fact]
    public async Task scalar_projection_of_an_absent_key_still_works()
    {
        await storeDocumentMissingTheNewerProperties();

        var value = await theSession.Query<Provider5461>()
            .Where(x => x.AgbCode == "abc")
            .Select(x => x.ExecuteWlzCheck)
            .FirstOrDefaultAsync();

        value.ShouldBeFalse();
    }

    [Fact]
    public async Task whole_document_load_of_an_absent_key_still_works()
    {
        var id = await storeDocumentMissingTheNewerProperties();

        var doc = await theSession.LoadAsync<Provider5461>(id);
        doc!.ExecuteWlzCheck.ShouldBeFalse();
    }

    [Fact]
    public async Task present_values_still_project()
    {
        theSession.Store(new Provider5461
        {
            Id = Guid.NewGuid(),
            AgbCode = "def",
            CareTypes = new List<string> { "one", "two" },
            ExecuteWlzCheck = true,
            Rank = 5
        });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Provider5461>()
            .Where(x => x.AgbCode == "def")
            .Select(x => new { x.CareTypes, x.ExecuteWlzCheck, x.Rank })
            .FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.ExecuteWlzCheck.ShouldBeTrue();
        result.Rank.ShouldBe(5);
        result.CareTypes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task explicitly_stored_null_in_a_dictionary_member_is_preserved()
    {
        theSession.Store(new Provider5461
        {
            Id = Guid.NewGuid(),
            AgbCode = "ghi",
            Rank = 1,
            Attributes = new Dictionary<string, string?> { { "a", null }, { "b", "yes" } }
        });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Provider5461>()
            .Where(x => x.AgbCode == "ghi")
            .Select(x => new { x.Attributes, x.Rank })
            .FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.Attributes.ShouldContainKey("a");
        result.Attributes["a"].ShouldBeNull();
    }
}

public class Provider5461
{
    public Guid Id { get; set; }
    public string AgbCode { get; set; } = string.Empty;
    public List<string> CareTypes { get; set; } = new();
    public bool ExecuteWlzCheck { get; set; }
    public int Rank { get; set; }
    public Dictionary<string, string?> Attributes { get; set; } = new();
}

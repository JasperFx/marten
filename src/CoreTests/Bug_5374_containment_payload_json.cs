using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Marten.Linq;
using MartenSystemTextJsonSerializer = Marten.Services.SystemTextJsonSerializer;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

// #5374: a filter over a child collection handed its jsonb payload — Marten's own dictionary of member
// names to compared values — to the store's serializer, which under Native AOT is a source-generated
// resolver that carries the consumer's documents and nothing else. JsonbPayload writes that shape
// itself, so the query no longer depends on the consumer having declared object[], the dictionary, or
// the enum it compares.
//
// A payload that differs from what the serializer wrote by one character matches nothing and reports it
// as "no rows", so every test here is the same assertion: byte-identical to the old path.
public class Bug_5374_containment_payload_json
{
    public enum Soort
    {
        Huisarts,
        Apotheek
    }

    public record Bedrag(decimal Amount, string Currency);

    private static Dictionary<string, object> payload() => new()
    {
        ["AgbCode"] = "01059910",
        ["Naam"] = "Praktijk Jansen & Zonen",
        ["Soort"] = Soort.Apotheek,
        ["Actief"] = true,
        ["Aantal"] = 3,
        ["Volgnummer"] = 42L,
        ["Tarief"] = 21.59m,
        ["Marge"] = 0.5d,
        ["Id"] = Guid.Parse("6d1a3f8e-5f2a-4f6d-9a7b-0b1c2d3e4f50"),
        ["Moment"] = new DateTimeOffset(2026, 9, 10, 12, 30, 0, TimeSpan.FromHours(2)),
        ["Gewijzigd"] = new DateTime(2026, 9, 10, 12, 30, 0, DateTimeKind.Utc),
        ["Peildatum"] = new DateOnly(2026, 9, 10),
        ["Aanvang"] = new TimeOnly(12, 30, 15),
        ["Ontbreekt"] = null,
        // How a query against a dictionary member renders: the nested dictionary is typed, not
        // Dictionary<string, object>, and it is still a JSON object rather than a list of pairs.
        ["Kenmerken"] = new Dictionary<string, string> { ["kleur"] = "blauw", ["maat"] = "L" },
        ["Aantallen"] = new Dictionary<int, string> { [1] = "een" },
        ["PerSoort"] = new Dictionary<Soort, decimal> { [Soort.Apotheek] = 21.59m },
        ["Regels"] = new object[]
        {
            new Dictionary<string, object> { ["Prestatiecode"] = "12000", ["Soort"] = Soort.Huisarts }
        }
    };

    [Theory]
    [InlineData(Casing.Default, EnumStorage.AsInteger)]
    [InlineData(Casing.Default, EnumStorage.AsString)]
    [InlineData(Casing.CamelCase, EnumStorage.AsInteger)]
    [InlineData(Casing.CamelCase, EnumStorage.AsString)]
    [InlineData(Casing.SnakeCase, EnumStorage.AsString)]
    public void writes_what_system_text_json_wrote(Casing casing, EnumStorage enumStorage)
    {
        var serializer = new MartenSystemTextJsonSerializer { Casing = casing, EnumStorage = enumStorage };

        JsonbPayload.ToJson(serializer, payload())
            .ShouldBe(serializer.ToCleanJson(payload()));
    }

    // The array wrapper is how ContainmentWhereFilter renders a filter over a child collection, which is
    // the shape the issue was reported against.
    [Fact]
    public void writes_what_system_text_json_wrote_for_a_collection_usage()
    {
        var serializer = new MartenSystemTextJsonSerializer { EnumStorage = EnumStorage.AsString };

        JsonbPayload.ToJson(serializer, new object[] { payload() })
            .ShouldBe(serializer.ToCleanJson(new object[] { payload() }));
    }

    // A renamed member is the one enum shape where the member's own name is the wrong answer.
    [Fact]
    public void writes_what_system_text_json_wrote_for_a_renamed_enum_member()
    {
        var serializer = new MartenSystemTextJsonSerializer { EnumStorage = EnumStorage.AsString };
        var data = new Dictionary<string, object> { ["Soort"] = Hernoemd.Apotheek };

        JsonbPayload.ToJson(serializer, data).ShouldBe(serializer.ToCleanJson(data));
    }

    // Newtonsoft is the other serializer this payload can be written with, and there the two differ by
    // escaping alone - System.Text.Json's encoder escapes an ampersand where Newtonsoft leaves it. That
    // cannot change a result: Postgres parses the parameter into jsonb, where the two are one character.
    [Fact]
    public void writes_what_newtonsoft_wrote_apart_from_escaping()
    {
        var serializer = new Marten.Services.JsonNetSerializer { EnumStorage = EnumStorage.AsString };

        JsonNode.DeepEquals(
                JsonNode.Parse(JsonbPayload.ToJson(serializer, payload())),
                JsonNode.Parse(serializer.ToCleanJson(payload())))
            .ShouldBeTrue();
    }

    public enum Hernoemd
    {
        Huisarts,

        [JsonStringEnumMemberName("apotheek-houdend")]
        Apotheek
    }

    // A value the writer does not render itself falls through to the serializer, which is the only thing
    // that knows about a converter the consumer registered.
    [Fact]
    public void writes_what_system_text_json_wrote_for_a_document_typed_value()
    {
        var serializer = new MartenSystemTextJsonSerializer { Casing = Casing.CamelCase };
        var data = new Dictionary<string, object> { ["Bedrag"] = new Bedrag(21.59m, "EUR") };

        JsonbPayload.ToJson(serializer, data).ShouldBe(serializer.ToCleanJson(data));
    }
}

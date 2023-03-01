using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Linq;

public class recognizing_json_names_from_attributes
{
    [Fact]
    public async Task recognize_newtonsoft_json_property_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
        });

        using var session = store.LightweightSession();

        var command = session.Query<AttributedDoc>().Where(x => x.Color == "Red")
            .ToCommand();

        command.CommandText.ShouldBe("select d.id, d.data from atts.mt_doc_attributeddoc as d where d.data ->> 'shade' = :p0");

    }

    [Fact]
    public async Task recognize_stj_json_property_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.Serializer<SystemTextJsonSerializer>();
        });

        using var session = store.LightweightSession();

        var command = session.Query<StjDoc>().Where(x => x.Color == "Red")
            .ToCommand();

        command.CommandText.ShouldBe("select d.id, d.data from atts.mt_doc_stjdoc as d where d.data ->> 'shade' = :p0");

    }
}

public class AttributedDoc
{
    public Guid Id { get; set; }

    [JsonProperty("shade")]
    public string Color { get; set; }
}

public class StjDoc
{
    public Guid Id { get; set; }

    [JsonPropertyName("shade")]
    public string Color { get; set; }
}

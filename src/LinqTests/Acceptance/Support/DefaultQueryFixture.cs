using System.Text.Json.Serialization;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit.Abstractions;

namespace LinqTests.Acceptance.Support;

public class DefaultQueryFixture: TargetSchemaFixture
{
    public DefaultQueryFixture()
    {
        Store = ProvisionStore("linq_querying");

        DuplicatedFieldStore = ProvisionStore("duplicate_fields", o =>
        {
            o.Schema.For<Target>()
                .Duplicate(x => x.Number)
                .Duplicate(x => x.Long)
                .Duplicate(x => x.String)
                .Duplicate(x => x.Date)
                .Duplicate(x => x.Double)
                .Duplicate(x => x.Flag)
                .Duplicate(x => x.Color)
                .Duplicate(x => x.NumberArray);
        });

        FSharpFriendlyStore = ProvisionStore("linq_querying", options =>
        {
            options.RegisterFSharpOptionValueTypes();
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            options.UseSystemTextJsonForSerialization(serializerOptions);
        });

        FSharpFriendlyStoreWithDuplicatedField = ProvisionStore("duplicate_fields", options =>
        {
            options.Schema.For<Target>()
                .Duplicate(x => x.Number)
                .Duplicate(x => x.Long)
                .Duplicate(x => x.String)
                .Duplicate(x => x.Date)
                .Duplicate(x => x.Double)
                .Duplicate(x => x.Flag)
                .Duplicate(x => x.Color)
                .Duplicate(x => x.NumberArray);

            options.RegisterFSharpOptionValueTypes();
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            options.UseSystemTextJsonForSerialization(serializerOptions);
        });

        SystemTextJsonStore = ProvisionStore("stj_linq", o =>
        {
            o.Serializer<SystemTextJsonSerializer>();
        });
    }

    public DocumentStore SystemTextJsonStore { get; set; }

    public DocumentStore DuplicatedFieldStore { get; set; }

    public DocumentStore FSharpFriendlyStore { get; set; }
    public DocumentStore FSharpFriendlyStoreWithDuplicatedField { get; set; }

    public DocumentStore Store { get; set; }
}

public static class DefaultQueryFixtureExtensions
{
    public static void UseFSharp(this DocumentStore store)
    {
        var o = store.Options;
        o.RegisterFSharpOptionValueTypes();
        var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
        o.UseSystemTextJsonForSerialization(serializerOptions);
    }
}

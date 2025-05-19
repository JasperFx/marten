using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit.Abstractions;

namespace LinqTests.Acceptance.Support;

public class DefaultQueryFixture: TargetSchemaFixture, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        Store = await ProvisionStoreAsync("linq_querying");

        DuplicatedFieldStore = await ProvisionStoreAsync("duplicate_fields", o =>
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

        FSharpFriendlyStore = await ProvisionStoreAsync("fsharp_linq_querying", options =>
        {
            options.RegisterFSharpOptionValueTypes();
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            options.UseSystemTextJsonForSerialization(serializerOptions);
        }, isFsharpTest: true);

        FSharpFriendlyStoreWithDuplicatedField = await ProvisionStoreAsync("fsharp_duplicated_fields", options =>
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
        }, isFsharpTest: true);

        SystemTextJsonStore = await ProvisionStoreAsync("stj_linq", o =>
        {
            o.Serializer<SystemTextJsonSerializer>();
        });
    }

    public async Task DisposeAsync()
    {

    }

    public DocumentStore SystemTextJsonStore { get; set; }

    public DocumentStore DuplicatedFieldStore { get; set; }

    public DocumentStore FSharpFriendlyStore { get; set; }
    public DocumentStore FSharpFriendlyStoreWithDuplicatedField { get; set; }

    public DocumentStore Store { get; set; }
}


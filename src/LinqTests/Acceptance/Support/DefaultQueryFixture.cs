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
        Store = await ProvisionStore("linq_querying");

        DuplicatedFieldStore = await ProvisionStore("duplicate_fields", o =>
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

        SystemTextJsonStore = await ProvisionStore("stj_linq", o =>
        {
            o.Serializer<SystemTextJsonSerializer>();
        });
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public DocumentStore SystemTextJsonStore { get; set; }

    public DocumentStore DuplicatedFieldStore { get; set; }

    public DocumentStore Store { get; set; }
}

using System;
using System.Threading.Tasks;
using Marten.Patching;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace PatchingTests.Patching;

public class patching_with_altered_metadata_columns : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public patching_with_altered_metadata_columns(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task write_and_read_metadata_from_patch()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().Metadata(m =>
            {
                m.Version.Enabled = false;
                m.LastModified.Enabled = false;
                m.CausationId.Enabled = true;
                m.CorrelationId.Enabled = true;
                m.LastModifiedBy.Enabled = true;

            });
        });

        var target1 = new Target { Color = Colors.Blue, Number = 1 };
        var target2 = new Target { Color = Colors.Blue, Number = 1 };
        var target3 = new Target { Color = Colors.Blue, Number = 1 };
        var target4 = new Target { Color = Colors.Green, Number = 1 };
        var target5 = new Target { Color = Colors.Green, Number = 1 };
        var target6 = new Target { Color = Colors.Red, Number = 1 };

        theSession.Store(target1, target2, target3, target4, target5, target6);
        await theSession.SaveChangesAsync();


        theSession.CorrelationId = "correlation1";
        theSession.CausationId = "causation1";
        theSession.LastModifiedBy = "some guy";

        theSession.Logger = new TestOutputMartenLogger(_output);
        theSession.Patch<Target>(target1.Id).Set(x => x.Number, 2);

        await theSession.SaveChangesAsync();

        var metadata = await theSession.MetadataForAsync(target1);

        metadata.CorrelationId.ShouldBe(theSession.CorrelationId);
        metadata.CausationId.ShouldBe(theSession.CausationId);
        metadata.LastModifiedBy.ShouldBe(theSession.LastModifiedBy);
    }
}

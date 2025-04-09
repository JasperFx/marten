using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Internal.Operations;
using Marten.Testing.Documents;
using NSubstitute;
using Xunit;

namespace DaemonTests.Internals;

public class AsyncOptionsTests
{
    [Fact]
    public void teardown_by_view_type_1()
    {
        var options = new AsyncOptions();
        options.DeleteViewTypeOnTeardown<Target>();
        options.DeleteViewTypeOnTeardown(typeof(User));


        var operations = Substitute.For<IDocumentOperations>();
        options.Teardown(operations);

        operations.Received().QueueOperation(new TruncateTable(typeof(Target)));
        operations.Received().QueueOperation(new TruncateTable(typeof(User)));
    }

}

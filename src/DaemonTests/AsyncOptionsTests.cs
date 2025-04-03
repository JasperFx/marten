using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Internal.Operations;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DaemonTests;

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

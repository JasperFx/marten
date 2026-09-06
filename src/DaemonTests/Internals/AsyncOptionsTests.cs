using System.Collections.Generic;
using System.Linq;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Internal.Operations;
using Marten.Testing.Documents;
using NSubstitute;
using Shouldly;
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
        options.Teardown(operations, new StoreOptions());

        operations.Received().QueueOperation(new TruncateTable(typeof(Target)));
        operations.Received().QueueOperation(new TruncateTable(typeof(User)));
    }

    /// <summary>
    /// #5329: a document type with custom projection storage (EF Core) keeps its rows somewhere
    /// other than <c>mt_doc_&lt;tdoc&gt;</c>, so teardown must not queue a truncate against a table
    /// Marten never created. The guard is keyed strictly on that registry — the sibling type here
    /// has no entry and must still be truncated, which is the half of this that would turn a rebuild
    /// into a silent no-op if it ever regressed.
    /// </summary>
    [Fact]
    public void teardown_skips_view_types_that_are_not_stored_by_marten()
    {
        var options = new AsyncOptions();
        options.DeleteViewTypeOnTeardown<Target>();
        options.DeleteViewTypeOnTeardown(typeof(User));

        var storeOptions = new StoreOptions();
        storeOptions.CustomProjectionStorageProviders[typeof(Target)] = (_, _) => new object();

        var operations = Substitute.For<IDocumentOperations>();
        options.Teardown(operations, storeOptions);

        operations.DidNotReceive().QueueOperation(new TruncateTable(typeof(Target)));
        operations.Received().QueueOperation(new TruncateTable(typeof(User)));
    }

    /// <summary>
    /// #5329: the per-tenant rebuild path shares the same cleanup list and needs the same guard.
    /// </summary>
    [Fact]
    public void teardown_for_tenant_skips_view_types_that_are_not_stored_by_marten()
    {
        var options = new AsyncOptions();
        options.DeleteViewTypeOnTeardown<Target>();
        options.DeleteViewTypeOnTeardown(typeof(User));

        var storeOptions = new StoreOptions();
        storeOptions.CustomProjectionStorageProviders[typeof(Target)] = (_, _) => new object();

        var operations = Substitute.For<IDocumentOperations>();
        var queued = new List<IStorageOperation>();
        operations.When(x => x.QueueOperation(Arg.Any<IStorageOperation>()))
            .Do(call => queued.Add(call.Arg<IStorageOperation>()));

        options.TeardownForTenant(operations, "blue", storeOptions);

        // DeleteAllForTenant has no value equality, so assert on what was actually queued
        queued.OfType<DeleteAllForTenant>().Select(x => x.DocumentType)
            .ShouldHaveSingleItem()
            .ShouldBe(typeof(User));
    }
}

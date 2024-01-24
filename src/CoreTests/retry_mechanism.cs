using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class retry_mechanism : IntegrationContext
{
    public retry_mechanism(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task can_successfully_retry()
    {
        var sometimesFailingOperation1 = new SometimesFailingOperation();
        theSession.QueueOperation(sometimesFailingOperation1);
        var sometimesFailingOperation2 = new SometimesFailingOperation();
        theSession.QueueOperation(sometimesFailingOperation2);

        await theSession.SaveChangesAsync();

        // Only succeeds on the 3rd try
        sometimesFailingOperation1.Usage.ShouldBe(3);
        sometimesFailingOperation2.Usage.ShouldBe(3);
    }

    [Fact]
    public void can_successfully_retry_sync()
    {
        var sometimesFailingOperation1 = new SometimesFailingOperation();
        theSession.QueueOperation(sometimesFailingOperation1);
        var sometimesFailingOperation2 = new SometimesFailingOperation();
        theSession.QueueOperation(sometimesFailingOperation2);

        theSession.SaveChanges();

        // Only succeeds on the 3rd try
        sometimesFailingOperation1.Usage.ShouldBe(3);
        sometimesFailingOperation2.Usage.ShouldBe(3);
    }
}

public class SometimesFailingOperation: IStorageOperation
{
    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        Usage++;

        builder.Append("select 1");
    }

    public int Usage { get; private set; } = 0;

    public Type DocumentType { get; }
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (Usage < 2)
        {
            throw new NpgsqlException();
        }
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (Usage < 2)
        {
            throw new NpgsqlException();
        }

        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }
}

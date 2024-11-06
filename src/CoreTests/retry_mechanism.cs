using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core.Operations;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class retry_mechanism : OneOffConfigurationsContext
{
    [Fact]
    public async Task can_successfully_retry()
    {
        StoreOptions(opts => opts.DatabaseSchemaName = "retries");

        var sometimesFailingOperation1 = new SometimesFailingOperation();
        theSession.QueueOperation(sometimesFailingOperation1);

        await theSession.SaveChangesAsync();

        // Only succeeds on the 3rd try
        sometimesFailingOperation1.Usage.ShouldBe(2);
    }

    [Fact]
    public async Task can_successfully_retry_sync()
    {
        StoreOptions(opts => opts.DatabaseSchemaName = "retries");

        var sometimesFailingOperation1 = new SometimesFailingOperation();
        theSession.QueueOperation(sometimesFailingOperation1);

        await theSession.SaveChangesAsync();

        // Only succeeds on the 3rd try
        sometimesFailingOperation1.Usage.ShouldBe(2);
    }
}

public class SometimesFailingOperation: IStorageOperation
{
    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("select 1");
    }

    public int Usage { get; private set; } = 0;

    public Type DocumentType { get; }
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        Usage++;
        if (Usage < 2)
        {
            throw new MartenCommandException(new NpgsqlCommand(), new Exception());
        }
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        Usage++;
        if (Usage < 2)
        {
            throw new MartenCommandException(new NpgsqlCommand(), new Exception());
        }

        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }
}

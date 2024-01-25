using System;
using Marten.Exceptions;
using Npgsql;
using Polly;

namespace Marten.Testing.Examples;

public class ErrorHandling
{
    public static void configure_polly()
    {
        #region sample_configure_polly

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            opts.ConfigurePolly(builder =>
            {
                builder.AddRetry(new()
                {
                    ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>().Handle<MartenCommandException>(),
                    MaxRetryAttempts = 10, // this is excessive, but just wanted to show something different
                    Delay = TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Linear
                });
            });
        });

        #endregion

    }
}

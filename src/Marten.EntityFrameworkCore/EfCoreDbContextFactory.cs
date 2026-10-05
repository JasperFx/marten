using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Internal helper to create DbContext instances for projection use.
/// The DbContext is created with an NpgsqlConnection from Marten's database.
/// The connection is swapped to the real transaction connection later by
/// <see cref="DbContextTransactionParticipant{TDbContext}"/> when the
/// transaction is ready.
///
/// <para>
/// #5601: this connection is handed over CLOSED, and nothing here opens it. It used to be opened
/// eagerly -- synchronously -- so that a <c>SET search_path</c> could be issued on it, which meant
/// every <c>SaveChangesAsync</c> with an inline EF Core projection held TWO pooled connections at
/// once: this one, released only in <see cref="DbContextTransactionParticipant{TDbContext}.BeforeCommitAsync" />,
/// plus the session's own commit connection, acquired after it. Held in that order they can never
/// queue cleanly: once concurrency reached <c>Maximum Pool Size</c> every connection in the pool was
/// one of these, every save was waiting for a commit connection only another save could release, and
/// nothing completed until the pool timeout. At pool size 1 a single save could not succeed at all.
/// </para>
///
/// <para>
/// Letting EF Core own the open means it happens on first use, asynchronously, honouring the
/// caller's CancellationToken, and -- the part that fixes the deadlock -- that EF closes it again
/// when each read finishes. By the time the participant swaps in the session's connection this one
/// is back in the pool, so a save needs one connection at a time, the same as a save with no
/// projection registered.
/// </para>
/// </summary>
internal static class EfCoreDbContextFactory
{
    public static (TDbContext DbContext, NpgsqlConnection InitialConnection) Create<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(
        this Storage.IMartenDatabase database,
        Action<DbContextOptionsBuilder<TDbContext>>? configure = null,
        string? schemaName = null)
        where TDbContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TDbContext>();

        // Create a connection from Marten's database for provider registration. Deliberately left
        // closed -- see the class doc.
        var connection = database.CreateConnection();

        builder.UseNpgsql(connection);

        // The schema still has to be set, but now on every open rather than once, because EF opens
        // and closes this connection around each read. An interceptor is the only place that can do
        // that: rewriting the connection string to carry Search Path would throw for a connection
        // handed out by an NpgsqlDataSource, which is how a store configured with
        // opts.Connection(dataSource) produces one.
        if (!string.IsNullOrEmpty(schemaName))
        {
            builder.AddInterceptors(new SearchPathInterceptor(schemaName));
        }

        configure?.Invoke(builder);
        var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), builder.Options)!;
        return (dbContext, connection);
    }
}

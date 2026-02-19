using System;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Internal helper to create DbContext instances for projection use.
/// The DbContext is created with an NpgsqlConnection from Marten's database.
/// The connection is swapped to the real transaction connection later by
/// <see cref="DbContextTransactionParticipant{TDbContext}"/> when the
/// transaction is ready.
/// </summary>
internal static class EfCoreDbContextFactory
{
    public static (TDbContext DbContext, NpgsqlConnection InitialConnection) Create<TDbContext>(
        Storage.IMartenDatabase database,
        Action<DbContextOptionsBuilder<TDbContext>>? configure = null)
        where TDbContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TDbContext>();

        // Create a connection object from Marten's database to register the Npgsql provider.
        // This connection is used only for provider configuration; the real connection is
        // set by BeforeCommitAsync when the transaction is ready.
        var connection = database.CreateConnection();
        builder.UseNpgsql(connection);
        configure?.Invoke(builder);
        var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), builder.Options)!;
        return (dbContext, connection);
    }
}

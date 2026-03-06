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
        this Storage.IMartenDatabase database,
        Action<DbContextOptionsBuilder<TDbContext>>? configure = null,
        string? schemaName = null)
        where TDbContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TDbContext>();

        // Create a connection from Marten's database for provider registration.
        var connection = database.CreateConnection();

        // If a schema name is provided, open the connection and set the search_path
        // so EF Core queries target the correct schema when it loads existing aggregates.
        if (!string.IsNullOrEmpty(schemaName))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET search_path TO {schemaName}";
            cmd.ExecuteNonQuery();
        }

        builder.UseNpgsql(connection);
        configure?.Invoke(builder);
        var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), builder.Options)!;
        return (dbContext, connection);
    }
}

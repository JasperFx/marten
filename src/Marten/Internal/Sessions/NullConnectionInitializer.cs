#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions;

/// <summary>
/// No-op <see cref="IConnectionInitializer"/> used when no per-connection
/// initialization is required.
/// </summary>
internal sealed class NullConnectionInitializer: IConnectionInitializer
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static readonly NullConnectionInitializer Instance = new();

    /// <inheritdoc />
    public void Initialize(NpgsqlConnection connection)
    {
    }

    /// <inheritdoc />
    public Task InitializeAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return Task.CompletedTask;
    }
}

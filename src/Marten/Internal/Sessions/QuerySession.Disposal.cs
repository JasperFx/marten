#nullable enable
using System;
using System.Threading.Tasks;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    private bool _disposed;

    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        releaseTransactionParticipants();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await releaseTransactionParticipantsAsync().ConfigureAwait(false);
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// #5228: a transaction participant can hold unmanaged resources -- the EF Core integration
    /// holds an open placeholder NpgsqlConnection -- that it only releases when its
    /// BeforeCommitAsync runs. Every path where SaveChangesAsync throws before, or during, that
    /// call would otherwise leak them. ITransactionParticipant deliberately does not require
    /// IAsyncDisposable (it is public API and most participants hold nothing), so this is a
    /// best-effort pass over the ones that opted in.
    /// </summary>
    private protected virtual ValueTask releaseTransactionParticipantsAsync() => default;

    private protected virtual void releaseTransactionParticipants()
    {
    }

    protected void assertNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, "This session has been disposed");
    }
}

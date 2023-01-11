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
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    protected void assertNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("This session has been disposed");
        }
    }
}

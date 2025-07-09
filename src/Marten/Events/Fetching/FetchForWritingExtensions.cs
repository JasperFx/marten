using System;
using Marten.Internal.Sessions;

namespace Marten.Events.Fetching;

internal static class FetchForWritingExtensions
{
    public static DocumentSessionBase? AssertIsDocumentSession(this QuerySession session)
    {
        if (session is not DocumentSessionBase)
        {
            throw new InvalidOperationException(
                "Using FetchForWriting() is only possible for full, writeable IDocumentSession");
        }

        return (DocumentSessionBase?)session;
    }
}

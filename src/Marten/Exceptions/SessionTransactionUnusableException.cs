#nullable enable
using System;

namespace Marten.Exceptions;

/// <summary>
///     Thrown when a session that holds one connection open for its whole lifetime --
///     <c>UseStickyConnectionLifetimes</c>, <see cref="System.Data.IsolationLevel.Serializable" />, a
///     caller-supplied connection, or an ambient/external transaction -- can no longer be used, because an
///     earlier failure left its connection or its transaction in a state nothing can recover.
/// </summary>
/// <remarks>
///     <para>
///         Two shapes reach here, and in both cases the damage was done by the <see cref="Exception.InnerException" />
///         rather than by whatever call happened to notice.
///     </para>
///     <para>
///         <b>The transaction is aborted (#5578).</b> Any statement that fails inside a transaction block aborts it,
///         and PostgreSQL then answers <c>25P02 in_failed_sql_transaction</c> to everything until the block ends.
///         A command timeout does this while leaving the connection perfectly healthy, so the session carried on
///         reporting a bare 25P02 that named neither the timeout nor the way out. Marten now names the original
///         failure instead.
///     </para>
///     <para>
///         <b>The connection is broken (#5577).</b> Npgsql reports a broken connection as
///         <see cref="System.Data.ConnectionState.Closed" />, so reopening it looks reasonable and is not: it
///         acquires a <i>different</i> backend, and the transaction the session was running in died with the old
///         one. Resuming there would silently run the rest of the session outside its transaction, at a different
///         snapshot. There is no way to put that back, so the session fails instead.
///     </para>
///     <para>
///         Recovery belongs above the session. Discard it, build a new one, and redo the work from its inputs --
///         a Wolverine message retry does exactly that. See the resiliency documentation.
///     </para>
/// </remarks>
public class SessionTransactionUnusableException: MartenException
{
    internal SessionTransactionUnusableException(string reason, Exception cause): base(
        $"This session cannot be used any further: {reason} See the inner exception for the original failure.",
        cause)
    {
    }
}

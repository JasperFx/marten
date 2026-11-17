using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

/// <summary>
///     #5935: the HiLo concurrency tests run on their own, after every parallel collection has finished.
/// </summary>
/// <remarks>
///     <para>
///     <c>Store()</c> assigns a numeric id synchronously, so these tests block thread-pool threads on purpose,
///     a dozen or more at once. The worst moment is the first id of a run: every writer reaches the database's
///     HiLo sequences together, one of them builds them by waiting synchronously on the asynchronous schema
///     update, and the rest wait on it. That update needs a pool thread to finish, and every pool thread is
///     waiting, so nothing in the test process runs until the pool has injected enough threads.
///     </para>
///     <para>
///     Measured in a 2-CPU container: a <c>Task.Delay(10)</c> heartbeat in another test came back 4.1 seconds
///     late while <c>concurrent_sessions_storing_documents_get_unique_ids</c> started up. In CI that test took up to
///     8 seconds, and unrelated tests running beside it slowed by up to 7.5. A command caught half way through
///     by the freeze outlives the 5-second command timeout of the test connection string even though the
///     server is idle: <c>TruncateTableTests</c> failed in CI with that timeout, and the same failure reproduced
///     locally in the <c>COPY</c> of its <c>BulkInsertAsync</c>. The server logged that test's <c>COPY</c> statements
///     running 4.5 seconds while it waited on the client, and no lock wait anywhere.
///     </para>
///     <para>
///     <c>DisableParallelization</c> keeps that starvation inside the tests that cause it. Raising the thread
///     pool's minimum instead would hide the blocking these tests exist to exercise.
///     </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class HiloThreadPoolCollection
{
    public const string Name = "hilo_thread_pool";
}

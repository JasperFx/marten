#nullable enable
using System;

namespace Marten.Services;

// NoDataReturnedCall moved to Weasel.Storage (#4821); AssertsOnCallback is a
// Marten-side unit-of-work callback marker and stays here.
public interface AssertsOnCallback{}

/// <summary>
/// #5502: an operation whose correctness depends on how many rows ITS OWN statement affected.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>DbDataReader.RecordsAffected</c> cannot answer that question. It is
/// <b>cumulative</b> across every statement the batch has executed so far, so an operation reading it
/// from inside <c>PostprocessAsync</c> sees other operations' row counts as if they were its own.
/// That is the "weird quirks of the combined statements" the old comment on
/// <c>UpdateProjectionProgress.PostprocessAsync</c> was describing: two progression updates in one
/// batch, the first affecting a row and the second affecting none, leave the reader reporting 1 when
/// the second operation is checked.
/// </para>
/// <para>
/// The per-statement count lives on <c>NpgsqlBatchCommand.RecordsAffected</c> instead, and
/// <see cref="Marten.Internal.Sessions.OperationPage" /> reads it there. It cannot be consulted from
/// <c>PostprocessAsync</c> even in principle: Npgsql populates it lazily as the reader advances, so an
/// operation's own count still reads -1 at the moment its callback runs. The check therefore happens
/// in a separate pass after the reader has been drained, which is why this is a distinct interface
/// rather than another <c>PostprocessAsync</c> responsibility.
/// </para>
/// <para>
/// Returning the problem rather than throwing it keeps the <i>policy</i> in one place. Today
/// <c>OperationPage</c> logs what this reports; the exception it hands back is the one that will be
/// thrown once that promotion happens in a major release.
/// </para>
/// </remarks>
public interface IAssertsRecordsAffected
{
    /// <summary>
    /// Inspect the number of rows this operation's own statement affected.
    /// </summary>
    /// <param name="recordsAffected">
    /// The per-statement count, or -1 when the database did not report one. Implementations must
    /// treat -1 as "unknown" and return null for it rather than reading it as zero.
    /// </param>
    /// <returns>The problem detected, or null when the count is acceptable.</returns>
    Exception? DetectRecordsAffectedProblem(int recordsAffected);
}

using Marten.Events.Daemon.Internals;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests.Events.Daemon;

// #4685 PR 1 -- pin the BatchFlushMode classifier's transition rule. Subsequent PRs in the
// series dispatch the BulkWriter (binary COPY) flush path on this signal; if the classifier
// ever over-classifies a Mixed batch as InsertOnly, a rebuild that should have used the
// per-row UPSERT path silently uses COPY instead and loses update / patch / delete semantics.
//
// Rule:
//   * Initial state -- InsertOnly (an empty batch is trivially insert-only; the BulkWriter
//     path is a no-op on zero rows).
//   * On Insert -- stay InsertOnly.
//   * On any other role -- transition to Mixed and stay there.
//   * Transition is monotonic -- once Mixed, never back to InsertOnly within the same batch.
public class BatchFlushModeClassifier_pin
{
    [Fact]
    public void initial_state_is_insert_only()
    {
        BatchFlushModeClassifier.Initial.ShouldBe(BatchFlushMode.InsertOnly);
    }

    [Fact]
    public void insert_keeps_insert_only()
    {
        var mode = BatchFlushModeClassifier.Initial;
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode.ShouldBe(BatchFlushMode.InsertOnly);
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode.ShouldBe(BatchFlushMode.InsertOnly);
    }

    [Theory]
    [InlineData(OperationRole.Update)]
    [InlineData(OperationRole.Upsert)]
    [InlineData(OperationRole.Patch)]
    [InlineData(OperationRole.Deletion)]
    [InlineData(OperationRole.Events)]
    [InlineData(OperationRole.Other)]
    public void any_non_insert_role_demotes_to_mixed(OperationRole role)
    {
        var mode = BatchFlushModeClassifier.Initial;
        mode = BatchFlushModeClassifier.WithOperation(mode, role);
        mode.ShouldBe(BatchFlushMode.Mixed);
    }

    [Fact]
    public void transition_to_mixed_is_monotonic()
    {
        // Once Mixed, even a string of inserts can't bring the batch back.
        var mode = BatchFlushMode.Mixed;
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode.ShouldBe(BatchFlushMode.Mixed);
    }

    [Fact]
    public void interleaved_insert_then_update_demotes()
    {
        var mode = BatchFlushModeClassifier.Initial;
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode.ShouldBe(BatchFlushMode.InsertOnly);
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Update);
        mode.ShouldBe(BatchFlushMode.Mixed);
        mode = BatchFlushModeClassifier.WithOperation(mode, OperationRole.Insert);
        mode.ShouldBe(BatchFlushMode.Mixed); // stays Mixed even after subsequent inserts
    }
}

using Marten.Internal.Operations;

namespace Marten.Internal.DirtyTracking
{
    /*
     * TODOs
     * SELECTOR DOES IT -- 0.) DirtyTrackingSession deals with building up the trackers on load?
     * DONE 1. Need to eject trackers for documents that are being ejected

     * DONE 3. The session needs to use trackers when building up the UpdateBatch
     * DONE 4. Need dirty checking selector
     * 4. See tests pass
     */

    public interface IChangeTracker
    {
        bool DetectChanges(IMartenSession session, out IStorageOperation operation);
        void Reset(IMartenSession session);

        object Document { get; }
    }
}

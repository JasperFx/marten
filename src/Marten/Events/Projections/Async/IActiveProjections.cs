using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Projections.Async
{
    public interface IActiveProjections : IDisposable
    {
        IProjectionTrack[] CoordinatedTracks { get; }

        IProjectionTrack[] AllTracks { get; }

        IProjectionTrack[] SelfGoverningTracks { get; set; }
        IProjectionTrack TrackFor(string viewType);
        void StartTracks(ITargetBlock<IDaemonUpdate> updates);
        Task StopAll();

        
    }
}
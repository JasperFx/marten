using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IActiveProjections
    {
        IProjectionTrack[] CoordinatedTracks { get; }

        IProjectionTrack[] AllTracks { get; }

        IProjectionTrack[] SelfGoverningTracks { get; set; }
        IProjectionTrack TrackFor(string viewType);
    }


    // Will have a stream that feeds into Daemon
    public class Daemon
    {
        private readonly Accumulator _accumulator = new Accumulator();
        private readonly IFetcher _fetcher;
        private readonly IActiveProjections _projections;

        // Should do this as a linked list. Make EventPage have a next?

        public Daemon(IDocumentStore store, IFetcher fetcher, IActiveProjections projections)
        {
            _fetcher = fetcher;
            _projections = projections;
        }

        public void Start()
        {
            //_fetcher.Start();
        }


        public async Task CachePage(EventPage page)
        {
            _accumulator.Store(page);

            // TODO -- make the threshold be configurable
            if (_accumulator.CachedEventCount > 10000)
            {
                var stop = _fetcher.Stop();
            }

            // TODO:
            /*
             * * store the page
             * * if this makes you go over the threshold for maximum items, latch the fetcher
             * * if it's the next page for any projection, send it on
             * 
             */
            throw new NotImplementedException();
        }

        public Task StoreProgress(string viewType, EventPage page)
        {
            // TODO -- Need to update projection status
            // * Trim off anything that's obsolete
            // * if under the maximum items stored, make sure that the fetcher is started
            // * if there is any more cached after this view, send the next page on
            // * if there are downstream projections from this one, send the page on
            //   to the next projection

            throw new NotImplementedException();
        }
    }
}
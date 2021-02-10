using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Testing.Linq;

namespace EventSourceWorker
{
    public class CreateNewTrip
    {
        public int Day { get; set; }
        public string State { get; set; }
        public Movement[] Movements { get; set; }
    }

    public class NewTripHandler
    {
        private readonly IDocumentSession _session;

        public NewTripHandler(IDocumentSession session)
        {
            _session = session;
        }

        public async Task Handle(CreateNewTrip trip)
        {
            var started = new TripStarted
            {
                Day = trip.Day
            };

            var departure = new Departure
            {
                Day = trip.Day,
                State = trip.State
            };

            var travel = new Travel
            {
                Day = trip.Day,
                Movements = new List<Movement>(trip.Movements)
            };

            // This will create a new event stream and
            // append the three events to that new stream
            // when the IDocumentSession is saved
            var action = _session.Events
                .StartStream(started, departure, travel);

            // You can also use strings as the identifier
            // for streams
            var tripId = action.Id;

            // Commit the events to the new event
            // stream for this trip
            await _session.SaveChangesAsync();
        }
    }

    public class EndTrip
    {
        public Guid TripId { get; set; }
        public bool Successful { get; set; }
        public string State { get; set; }
        public int Day { get; set; }
    }

    public class EndTripHandler
    {
        private readonly IDocumentSession _session;

        public EndTripHandler(IDocumentSession session)
        {
            _session = session;
        }

        public async Task Handle(EndTrip end)
        {
            // we need to first see the current
            // state of the Trip to decide how
            // to proceed, so load the pre-built
            // projected document from the database
            var trip = await _session
                .LoadAsync<Trip>(end.TripId);

            // finish processing the EndTrip command...
        }
    }
}

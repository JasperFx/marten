using System.Linq;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
	public class ScenarioCopyAndReplaceStream : IntegrationContext
	{
        public ScenarioCopyAndReplaceStream(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(options =>
            {
            	options.Events.StreamIdentity = StreamIdentity.AsString;
            });
        }

		[Fact]
		public void SampleCopyAndTransformStream()
		{
			// SAMPLE: scenario-copyandtransformstream-setup
			var started = new QuestStarted { Name = "Find the Orb" };
			var joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
			var slayed1 = new MonsterSlayed { Name = "Troll" };
			var slayed2 = new MonsterSlayed { Name = "Dragon" };

			using (var session = theStore.OpenSession())
			{
				session.Events.StartStream<Quest>(started.Name,started, joined, slayed1, slayed2);
				session.SaveChanges();
			}
			// ENDSAMPLE

			// SAMPLE: scenario-copyandtransformstream-transform
			using (var session = theStore.OpenSession())
			{
				var events = session.Events.FetchStream(started.Name);

				var transformedEvents = events.SelectMany(x =>
				{
					switch (x.Data)
					{
						case MonsterSlayed monster:
						{
							// Trolls we remove from our transformed stream
							return monster.Name.Equals("Troll") ? new object[] { } : new[] { monster };
						}
						case MembersJoined members:
						{
							// MembersJoined events we transform into a series of events
							return MemberJoined.From(members);
						}
					}

					return new[] { x.Data };
				}).Where(x => x != null).ToArray();

				var moveTo = $"{started.Name} without Trolls";
				// We copy the transformed events to a new stream
				session.Events.StartStream<Quest>(moveTo, transformedEvents);
				// And additionally mark the old stream as moved. Furthermore, we assert on the new expected stream version to guard against any racing updates
				session.Events.Append(started.Name, events.Count + 1, new StreamMovedTo
				{
					To = moveTo
				});

				// Transactionally update the streams.
				session.SaveChanges();
			}
			// ENDSAMPLE
		}

		// SAMPLE: scenario-copyandtransformstream-newevent
		public class MemberJoined
		{
			public int Day { get; set; }
			public string Location { get; set; }
			public string Name { get; set; }

			public MemberJoined()
			{
			}

			public MemberJoined(int day, string location, string name)
			{
				Day = day;
				Location = location;
				Name = name;
			}

			public static MemberJoined[] From(MembersJoined @event)
			{
				return @event.Members.Select(x => new MemberJoined(@event.Day, @event.Location, x)).ToArray();
			}
		}
		// ENDSAMPLE

		// SAMPLE: scenario-copyandtransformstream-streammoved
		public class StreamMovedTo
		{
			public string To { get; set; }
		}
		// ENDSAMPLE
	}
}

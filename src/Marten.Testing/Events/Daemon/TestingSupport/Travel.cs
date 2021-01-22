using System.Collections.Generic;
using System.Linq;

namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class Travel : IDayEvent
    {
        public static Travel Random(int day)
        {
            var travel = new Travel {Day = day,};

            var length = TripStream.Random.Next(1, 20);
            for (var i = 0; i < length; i++)
            {
                var movement = new Movement
                {
                    Direction = TripStream.RandomDirection(), Distance = TripStream.Random.NextDouble() * 100
                };

                travel.Movements.Add(movement);
            }

            return travel;
        }

        public int Day { get; set; }
        public IList<Movement> Movements { get; set; } = new List<Movement>();

        public double TotalDistance()
        {
            return Movements.Sum(x => x.Distance);
        }
    }
}

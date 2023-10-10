using System.Collections.Generic;
using System.Linq;

namespace Marten.AsyncDaemon.Testing.TestingSupport;

public class Travel : IDayEvent
{
    public static Travel Random(int day)
    {
        var travel = new Travel {Day = day,};

        var numberOfMovements = TripStream.Random.Next(1, 20);
        for (var i = 0; i < numberOfMovements; i++)
        {
            var movement = new Movement
            {
                Direction = TripStream.RandomDirection(), Distance = TripStream.Random.Next(500, 3000) / 100
            };

            travel.Movements.Add(movement);
        }

        var numberOfStops = TripStream.Random.Next(1, 10);
        for (var i = 0; i < numberOfStops; i++)
        {
            travel.Stops.Add(new Stop()
            {
                Time = TripStream.RandomTime(),
                State = TripStream.RandomState(),
                Duration = TripStream.Random.Next(10, 30)
            });
        }

        return travel;
    }

    public int Day { get; set; }

    #region sample_Travel_Movements

    public IList<Movement> Movements { get; set; } = new List<Movement>();
    public List<Stop> Stops { get; set; } = new();

    #endregion

    public double TotalDistance()
    {
        return Movements.Sum(x => x.Distance);
    }
}

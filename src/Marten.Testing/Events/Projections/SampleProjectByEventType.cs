using System;
using System.Linq;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class SampleProjectByEventType : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void CanProjectByEventType()
        {
            // SAMPLE: sample-project-by-event-type
            StoreOptions(storeOptions =>
            {
                // We index our streams by strings
                storeOptions.Events.StreamIdentity = StreamIdentity.AsString;                

                // Build candles in 1 second windows
                storeOptions.Events.InlineProjections.Add(new CandleProjection(TimeSpan.FromSeconds(1)));
                // Build candles for 5 transactions
                storeOptions.Events.InlineProjections.Add(new CandleProjection(5));
            });

            var random = new Random();
            var symbols = new[] { "AAPL", "GOOG", "MSFT" };

            var ticks = Enumerable.Range(1, 15)
                .Select(i => new Tick(DateTime.UtcNow.AddMinutes(i), symbols[random.Next(symbols.Length)], random.Next(100 * i)))
                .ToList();

            using (var s = theStore.OpenSession())
            {
                foreach (var tick in ticks)
                {
                    // We index the streams by the name of the symbol
                    s.Events.Append(tick.Symbol, tick);
                }
                s.SaveChanges();
            }

            var maxPrice = ticks.Max(x => x.Price);
            var maxTick = ticks.First(x => x.Price == maxPrice);

            using (var s = theStore.QuerySession())
            {
                // We should have a candle with the maximum from our maxTick
                var candle = s.Query<Candle>().Where(x => x.Symbol == maxTick.Symbol).OrderByDescending(x => x.High).First();
                var count = s.Query<Candle>().Count();

                Assert.Equal(maxTick.Price, candle.High);
                // 15*1 second windows + 3*5 transactions
                Assert.Equal(18, count);
            }
            // ENDSAMPLE
        }
    }

    // SAMPLE: sample-type-candle   
    public sealed class Candle
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
    // ENDSAMPLE

    // SAMPLE: sample-candleprojection
    public sealed class CandleProjection : ViewProjection<Candle, string>
    {
        public CandleProjection(int transactions)
        {
            // We project from the event Tick
            ProjectEvent<Tick>(
                (session, tick) =>
                {
                    // We check the number of events from our stream and assume it as the transaction count
                    var version = session.Events.FetchStreamState(tick.Symbol)?.Version ?? 0;
                    // We use the transaction count to ensure our candle holds at most data for specified number of transactions
                    return tick.Symbol + (version / transactions).ToString();
                },
                OnTick);
        }

        public CandleProjection(TimeSpan window)
        {
            // We project from the event Tick
            // We use the specified time window to ensure our candle holds at most data for window length of ticks
            ProjectEvent<Tick>(tick => tick.Symbol + tick.Time.Ticks / window.Ticks, OnTick);
        }

        // Update our candle with the tick data
        private void OnTick(Candle candle, Tick tick)
        {
            candle.Symbol = tick.Symbol;

            if (!candle.From.HasValue)
            {
                candle.From = tick.Time;
                candle.Open = candle.Low = candle.High = tick.Price;
            }

            if (tick.Price < candle.Low)
            {
                candle.Low = tick.Price;
            }

            if (tick.Price > candle.High)
            {
                candle.High = tick.Price;
            }

            candle.To = tick.Time;
            candle.Close = tick.Price;
        }
    }
    // ENDSAMPLE

    // SAMPLE: sample-type-tick
    public sealed class Tick
    {
        public Guid Id { get; set; }
        public readonly DateTime Time;
        public readonly decimal Price;
        public readonly string Symbol;        
        public Tick(DateTime time, string symbol, decimal price)
        {
            Time = time;
            Price = price;
            Symbol = symbol;
        }
    }
    // ENDSAMPLE
}
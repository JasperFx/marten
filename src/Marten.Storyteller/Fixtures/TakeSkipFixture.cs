using System;
using System.Linq;
using Baseline;

namespace Marten.Storyteller.Fixtures
{
    public class TakeSkipFixture : MatchedFixture
    {
        public static void TryIt()
        {
            var fixture = new TakeSkipFixture();

            fixture.GetSelectionValues("Sync").Each(x => Console.WriteLine(x));

            Console.WriteLine(fixture);
        }

        public TakeSkipFixture() : base("Take() and Skip()")
        {
            Sync += docs => docs.OrderBy(x => x.Long).Take(10).Skip(20);
            Sync += docs => docs.OrderBy(x => x.Long).Take(20).Skip(10);
            Sync += docs => docs.OrderBy(x => x.Long).Take(20);
            Sync += docs => docs.OrderBy(x => x.Long).Skip(15);

        }
    }
}
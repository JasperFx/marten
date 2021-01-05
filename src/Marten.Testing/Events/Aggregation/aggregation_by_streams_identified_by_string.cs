using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace Marten.Testing.Events.Aggregation
{
    [Collection("string_streams")]
    public class aggregation_by_streams_identified_by_string : OneOffConfigurationsContext
    {
        public aggregation_by_streams_identified_by_string() : base("string_streams")
        {
        }

        [Fact]
        public async Task end_to_end_aggregation()
        {
            StoreOptions(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString;
                x.Events.Projections.InlineSelfAggregate<NamedAggregate>();
            });

            theSession.Events.StartStream("first", new AEvent(), new AEvent(), new BEvent());
            theSession.Events.StartStream("second", new CEvent(), new CEvent(), new BEvent());

            await theSession.SaveChangesAsync();

            var first = await theSession.LoadAsync<NamedAggregate>("first");
            var second = await theSession.LoadAsync<NamedAggregate>("second");

            first.ShouldNotBeNull();
            first.A.ShouldBe(2);
            first.B.ShouldBe(1);

            second.B.ShouldBe(1);
            second.C.ShouldBe(2);
        }
    }

    public class NamedAggregate
    {
        public string Id { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }

        public void Apply(AEvent e) => A++;
        public void Apply(BEvent e) => B++;
        public void Apply(CEvent e) => C++;
    }
}

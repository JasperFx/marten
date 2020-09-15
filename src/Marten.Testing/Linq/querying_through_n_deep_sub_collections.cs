using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Pagination;
using Marten.Testing.Documents;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Linq
{

    public class querying_through_n_deep_sub_collections : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public class Top
        {
            public Guid Id { get; set; }
            public IList<Middle> Middles { get; set; } = new List<Middle>();

            public Top WithMiddle(Colors color, params string[] names)
            {
                var middle = new Middle{Color = color};
                Middles.Add(middle);

                middle.WithBottoms(names);

                return this;
            }

            protected bool Equals(Top other)
            {
                return Id.Equals(other.Id);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Top) obj);
            }

            public override int GetHashCode()
            {
                return Id.GetHashCode();
            }
        }

        public class Middle
        {
            public Colors Color { get; set; }

            public IList<Bottom> Bottoms { get; set; } = new List<Bottom>();

            public void WithBottoms(params string[] names)
            {
                foreach (var name in names)
                {
                    Bottoms.Add(new Bottom{Name = name});
                }
            }
        }

        public class Bottom
        {
            public string Name { get; set; }
        }

        private Top blueBill = new Top().WithMiddle(Colors.Blue, "Jack", "Bill");
        private Top top2 = new Top().WithMiddle(Colors.Blue, "Jill");
        private Top top3 = new Top().WithMiddle(Colors.Blue, "John");
        private Top greenBill = new Top().WithMiddle(Colors.Green, "James", "Bill");
        private Top top5 = new Top().WithMiddle(Colors.Blue, "Jimmy");
        private Top top6 = new Top().WithMiddle(Colors.Green, "Jake");
        private Top topNoBottoms = new Top().WithMiddle(Colors.Blue);

        public querying_through_n_deep_sub_collections(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;

            theStore.BulkInsert(new Top[]{blueBill, top2, top3, greenBill, top5, top6, topNoBottoms});

            theSession.Logger = new TestOutputMartenLogger(_output);
        }

        [Fact]
        public void can_query_by_any()
        {


            var results = theSession.Query<Top>().Where(x => x.Middles.Any(b => b.Bottoms.Any())).ToList();
            results.Any(x => x.Equals(blueBill)).ShouldBeTrue();
            results.Any(x => x.Equals(topNoBottoms)).ShouldBeFalse();

        }

        [Fact]
        public void query_inside_of_child_collections_collection()
        {
            var results = theSession.Query<Top>().Where(x =>
                x.Middles.Any(m => m.Color == Colors.Green && m.Bottoms.Any(b => b.Name == "Bill")));

            results.Single().ShouldBe(greenBill);
        }

        [Fact]
        public void query_inside_of_child_collections_collection_2()
        {
            var results = theSession.Query<Top>().Where(x => x.Middles.Any(m => m.Bottoms.Any(b => b.Name.StartsWith("B"))))
                .ToList();

            results.Count.ShouldBe(2);

            results.Any(x => x.Equals(blueBill)).ShouldBeTrue();
            results.Any(x => x.Equals(greenBill)).ShouldBeTrue();
        }
    }
}

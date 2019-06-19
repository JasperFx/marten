using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1256_querying_against_a_uint_type: IntegratedFixture
    {
        public class DocWithUint
        {
            public Guid Id { get; set; }
            public uint Number { get; set; }
        }

        [Fact]
        public void can_use_in_where_clauses()
        {
            var doc1 = new DocWithUint { Number = 1 };
            var doc2 = new DocWithUint { Number = 2 };
            var doc3 = new DocWithUint { Number = 3 };
            var doc4 = new DocWithUint { Number = 4 };
            var doc5 = new DocWithUint { Number = 5 };

            using (var session = theStore.LightweightSession())
            {
                session.Store(doc1, doc2, doc3, doc4, doc5);
                session.SaveChanges();

                session.Query<DocWithUint>().Count(x => x.Number > 3).ShouldBe(2);
            }
        }
    }
}

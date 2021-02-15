using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_484_Contains_on_IList_of_string: BugIntegrationContext
    {
        public class DocWithLists
        {
            public Guid Id { get; set; }

            public IList<string> Names { get; set; } = new List<string>();
        }

        public class DocWithLists2
        {
            public Guid Id { get; set; }

            public IReadOnlyList<string> Names { get; set; } = new List<string>();
        }

        [Fact]
        public void can_do_contains_against_IList()
        {
            var doc1 = new DocWithLists {Names = new List<string> {"Jeremy", "Josh", "Corey"}};
            var doc2 = new DocWithLists {Names = new List<string> {"Jeremy", "Lindsey", "Max"}};
            var doc3 = new DocWithLists {Names = new List<string> {"Jack", "Lindsey", "Max"}};

            using (var session = theStore.OpenSession())
            {
                session.Store(doc1, doc2, doc3);
                session.SaveChanges();

                var ids = session.Query<DocWithLists>().Where(x => x.Names.Contains("Jeremy")).Select(x => x.Id)
                    .ToList();

                ids.Count.ShouldBe(2);
                ids.ShouldContain(doc1.Id);
                ids.ShouldContain(doc2.Id);
            }
        }

        [Fact]
        public void can_do_contains_against_IList_with_camel_casing()
        {
            StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

            var doc1 = new DocWithLists {Names = new List<string> {"Jeremy", "Josh", "Corey"}};
            var doc2 = new DocWithLists {Names = new List<string> {"Jeremy", "Lindsey", "Max"}};
            var doc3 = new DocWithLists {Names = new List<string> {"Jack", "Lindsey", "Max"}};

            using (var session = theStore.OpenSession())
            {
                session.Store(doc1, doc2, doc3);
                session.SaveChanges();

                var ids = session.Query<DocWithLists>().Where(x => x.Names.Contains("Jeremy")).Select(x => x.Id)
                    .ToList();

                ids.Count.ShouldBe(2);
                ids.ShouldContain(doc1.Id);
                ids.ShouldContain(doc2.Id);
            }
        }
    }
}

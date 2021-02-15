using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_is_in_generic_enumerable_Tests : IntegrationContext
    {
        [Fact]
        public void can_query_against_number_in_iList()
        {
            var doc1 = new DocWithNumber { Number = 1 };
            var doc2 = new DocWithNumber { Number = 2 };
            var doc3 = new DocWithNumber { Number = 3 };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            IList<int> searchValues = new List<int> {2, 4, 5};

            theSession.Query<DocWithNumber>().Where(x=>searchValues.Contains(x.Number)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc2.Id);
        }

        [Fact]
        public void can_query_against_number_in_List()
        {
            var doc1 = new DocWithNumber { Number = 1 };
            var doc2 = new DocWithNumber { Number = 2 };
            var doc3 = new DocWithNumber { Number = 3 };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            List<int> searchValues = new List<int> { 2, 4, 5 };

            theSession.Query<DocWithNumber>().Where(x => searchValues.Contains(x.Number)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc2.Id);
        }

        public class DocWithNumber
        {
            public Guid Id { get; set; }
            public int Number { get; set; }
        }

        public query_with_is_in_generic_enumerable_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }


}

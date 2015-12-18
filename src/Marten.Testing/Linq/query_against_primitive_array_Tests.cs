using System;
using System.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_against_primitive_array_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void query_against_number_array()
        {
            var doc1 = new DocWithArrays {Numbers = new int[] {1, 2, 3}};
            var doc2 = new DocWithArrays {Numbers = new int[] {3, 4, 5}};
            var doc3 = new DocWithArrays {Numbers = new int[] {5, 6, 7}};


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Numbers.Contains(3)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

        [Fact]
        // SAMPLE: query_against_string_array
        public void query_against_string_array()
        {
            var doc1 = new DocWithArrays { Strings = new string[] {"a", "b", "c"} };
            var doc2 = new DocWithArrays { Strings = new string[] {"c", "d", "e"} };
            var doc3 = new DocWithArrays { Strings = new string[] {"d", "e", "f"} };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Strings.Contains("c")).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }
        // ENDSAMPLE

        [Fact]
        public void query_against_date_array()
        {
            var doc1 = new DocWithArrays {Dates = new [] {DateTime.Today, DateTime.Today.AddDays(1), DateTime.Today.AddDays(2)}};
            var doc2 = new DocWithArrays {Dates = new [] {DateTime.Today.AddDays(2), DateTime.Today.AddDays(3), DateTime.Today.AddDays(4)}};
            var doc3 = new DocWithArrays {Dates = new [] {DateTime.Today.AddDays(4), DateTime.Today.AddDays(5), DateTime.Today.AddDays(6)}};


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Dates.Contains(DateTime.Today.AddDays(2))).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }
    }

    public class DocWithArrays
    {
        public Guid Id;

        public int[] Numbers;

        public string[] Strings;

        public DateTime[] Dates;
    }
}
using System;
using System.Collections.Generic;
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


            theSession.Store(doc1, doc2, doc3);

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
        public void query_against_string_array_with_Any()
        {
            var doc1 = new DocWithArrays { Strings = new string[] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new string[] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new string[] { "d", "e", "f" } };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Strings.Any(_ => _ == "c")).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

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

        [Fact]
        public void query_against_number_list()
        {
            var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7 } };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists>().Where(x => x.Numbers.Contains(3)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }


        [Fact]
        public void query_against_number_list_with_any()
        {
            var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7 } };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists>().Where(x => x.Numbers.Any(_ => _ == 3)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

        [Fact]
        public void query_against_number_IList()
        {
            var doc1 = new DocWithLists2 { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists2 { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists2 { Numbers = new List<int> { 5, 6, 7 } };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists2>().Where(x => x.Numbers.Contains(3)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

        [Fact]
        public void query_against_number_IEnumerable()
        {
            var doc1 = new DocWithLists3 { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists3 { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists3 { Numbers = new List<int> { 5, 6, 7 } };


            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists3>().Where(x => x.Numbers.Contains(3)).ToArray()
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

    public class DocWithLists
    {
        public Guid Id;

        public List<int> Numbers;

    }

    public class DocWithLists2
    {
        public Guid Id;

        public IList<int> Numbers;

    }

    public class DocWithLists3
    {
        public Guid Id;

        public IEnumerable<int> Numbers;

    }
}
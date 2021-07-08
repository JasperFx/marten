using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_against_child_collections_integrated_Tests : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public query_against_child_collections_integrated_Tests(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
            StoreOptions(_ => _.UseDefaultSerialization(EnumStorage.AsString));
        }

        private Target[] targets;

        private void buildUpTargetData()
        {
            targets = Target.GenerateRandomData(20).ToArray();
            targets.SelectMany(x => x.Children).Each(x => x.Number = 5);

            targets[5].Children[0].Number = 6;
            targets[9].Children[0].Number = 6;
            targets[12].Children[0].Number = 6;

            targets[5].Children[0].Double = -1;
            targets[9].Children[0].Double = -1;
            targets[12].Children[0].Double = 10;

            targets[10].Color = Colors.Green;

            var child = targets[10].Children[0];
            child.Color = Colors.Blue;
            child.Inner = new Target
            {
                Number = -2,
                Color = Colors.Blue
            };

            theSession.Store(targets);

            theSession.SaveChanges();
        }

        [Fact]
        public void can_query_with_containment_operator()
        {
            buildUpTargetData();

            var expected = new[] { targets[5].Id, targets[9].Id, targets[12].Id }.OrderBy(x => x);

            theSession.Query<Target>("where data @> '{\"Children\": [{\"Number\": 6}]}'")
                .ToArray()
                .Select(x => x.Id).OrderBy(x => x)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Fact]
        public void can_query_with_an_any_operator()
        {
            buildUpTargetData();

            #region sample_any-query-through-child-collections
            var results = theSession.Query<Target>()
                .Where(x => x.Children.Any(_ => _.Number == 6))
                .ToArray();
            #endregion sample_any-query-through-child-collections

            results
                .Select(x => x.Id)
                .OrderBy(x => x)
                .ShouldHaveTheSameElementsAs(new[] { targets[5].Id, targets[9].Id, targets[12].Id }.OrderBy(x => x));
        }

        [Fact]
        public void can_query_with_an_any_operator_that_does_a_multiple_search_within_the_collection()
        {
            buildUpTargetData();

            #region sample_any-query-through-child-collection-with-and
            var results = theSession
                .Query<Target>()
                .Where(x => x.Children.Any(_ => _.Number == 6 && _.Double == -1))
                .ToArray();
            #endregion sample_any-query-through-child-collection-with-and

            results
                .Select(x => x.Id)
                .OrderBy(x => x)
                .ShouldHaveTheSameElementsAs(new[] { targets[5].Id, targets[9].Id }.OrderBy(x => x));
        }

        [Fact]
        public void can_query_on_deep_properties()
        {
            buildUpTargetData();

            theSession.Query<Target>()
                .Single(x => Enumerable.Any<Target>(x.Children, _ => _.Inner.Number == -2))
                .Id.ShouldBe(targets[10].Id);
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger)]
        [InlineData(EnumStorage.AsString)]
        public void can_query_on_enum_properties(EnumStorage enumStorage)
        {
            StoreOptions(_ => _.UseDefaultSerialization(enumStorage));
            buildUpTargetData();

            theSession.Query<Target>()
                .Count(x => Enumerable.Any<Target>(x.Children, _ => _.Color == Colors.Green))
                .ShouldBeGreaterThanOrEqualTo(1);
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger)]
        [InlineData(EnumStorage.AsString)]
        public void can_query_on_deep_enum_properties(EnumStorage enumStorage)
        {
            StoreOptions(_ => _.UseDefaultSerialization(enumStorage));
            buildUpTargetData();

            theSession.Query<Target>()
                .Count(x => x.Children.Any<Target>(_ => _.Inner.Color == Colors.Blue))
                .ShouldBeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public void Bug_503_child_collection_query_in_compiled_query()
        {
            using (var session = theStore.OpenSession())
            {
                var outer = new Outer();
                outer.Inners.Add(new Inner { Type = "T1", Value = "V11" });
                outer.Inners.Add(new Inner { Type = "T1", Value = "V12" });
                outer.Inners.Add(new Inner { Type = "T2", Value = "V21" });

                session.Store(outer);
                session.SaveChanges();
            }

            using (var session2 = theStore.OpenSession())
            {
                // This works
                var o1 = session2.Query<Outer>().First(o => o.Inners.Any(i => i.Type == "T1" && i.Value == "V12"));
                SpecificationExtensions.ShouldNotBeNull(o1);

                var o2 = session2.Query(new FindOuterByInner("T1", "V12"));

                SpecificationExtensions.ShouldNotBeNull(o2);

                o2.Id.ShouldBe(o1.Id);
            }
        }

        public class Outer
        {
            public Guid Id { get; set; }

            public IList<Inner> Inners { get; } = new List<Inner>();
        }

        public class Inner
        {
            public string Type { get; set; }

            public string Value { get; set; }
        }

        public class FindOuterByInner : ICompiledQuery<Outer, Outer>
        {
            public string Type { get; private set; }

            public string Value { get; private set; }

            public FindOuterByInner(string type, string value)
            {
                this.Type = type;
                this.Value = value;
            }

            public Expression<Func<IMartenQueryable<Outer>, Outer>> QueryIs()
            {
                return q => q.FirstOrDefault(o => o.Inners.Any(i => i.Type == Type && i.Value == Value));
            }
        }

        [Fact]
        public void Bug_415_can_query_when_matched_value_has_quotes()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Course>().Any(x => x.ExtIds.Contains("some'thing")).ShouldBeFalse();
            }
        }

        [Fact]
        public void Bug_415_can_query_inside_of_non_primitive_collection()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Course>().Any(x => x.Sources.Any(_ => _.Case == "some'thing"));
            }
        }

        public class Course
        {
            public Guid Id { get; set; }

            public string[] ExtIds { get; set; }

            public IList<Source> Sources { get; set; }
        }

        public class Source
        {
            public string Case { get; set; }
        }

        private Guid[] favAuthors = new Guid[] { Guid.NewGuid(), Guid.NewGuid() };

        private void buildAuthorData()
        {
            // test fixtures
            theSession.Store(new Article
            {
                Long = 1,
                CategoryArray = new [] { "sports", "finance", "health" },
                CategoryList = new List<string> { "sports", "finance", "health" },
                AuthorArray = favAuthors,
                Published = true,
            });

            theSession.Store(new Article
            {
                Long = 2,
                CategoryArray = new [] { "sports", "astrology" },
                AuthorArray = favAuthors.Take(1).ToArray(),
            });

            theSession.Store(new Article
            {
                Long = 3,
                CategoryArray = new [] { "health", "finance" },
                CategoryList = new List<string> { "sports", "health" },
                AuthorArray = favAuthors.Skip(1).ToArray(),
            });

            theSession.Store(new Article
            {
                Long = 4,
                CategoryArray = new [] { "health", "astrology" },
                AuthorList = new List<Guid> { Guid.NewGuid() },
                Published = true,
            });

            theSession.Store(new Article
            {
                Long = 5,
                CategoryArray = new [] { "sports", "nested" },
                AuthorList = new List<Guid> { Guid.NewGuid(), favAuthors[1] },
            });

            theSession.Store(new Article
            {
                Long = 6,
                AuthorArray = new Guid[] { favAuthors[0], Guid.NewGuid() },
                ReferencedArticle = new Article
                {
                    CategoryArray = new [] { "nested" },
                }
            });
            theSession.SaveChanges();
        }

        [Fact]
        public void query_string_array_intersects_array()
        {
            buildAuthorData();

            var interests = new [] { "finance", "astrology" };
            var res = theSession.Query<Article>()
                .Where(x => x.CategoryArray.Any(s => interests.Contains(s)))
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(4);
            res[0].Long.ShouldBe(1);
            res[1].Long.ShouldBe(2);
            res[2].Long.ShouldBe(3);
            res[3].Long.ShouldBe(4);
        }

        [Fact]
        public void query_string_list_intersects_array()
        {
            buildAuthorData();

            var interests = new [] { "health", "astrology" };
            var res = theSession.Query<Article>()
                .Where(x => x.CategoryList.Any(s => interests.Contains(s)))
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(2);
            res[0].Long.ShouldBe(1);
            res[1].Long.ShouldBe(3);
        }

        [Fact]
        public void query_nested_string_array_intersects_array()
        {
            buildAuthorData();

            var interests = new [] { "nested" };
            var res = theSession.Query<Article>()
                .Where(x => x.ReferencedArticle.CategoryArray.Any(s => interests.Contains(s)))
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(1);
            res[0].Long.ShouldBe(6);
        }

        [Fact]
        public void query_string_array_intersects_array_with_boolean_and()
        {
            buildAuthorData();

            var interests = new [] { "finance", "astrology" };
            var res = theSession.Query<Article>()
                .Where(x => x.CategoryArray.Any(s => interests.Contains(s)) && x.Published)
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(2);
            res[0].Long.ShouldBe(1);
            res[1].Long.ShouldBe(4);
        }

        [Fact]
        public void query_guid_array_intersects_array()
        {
            buildAuthorData();

            var res = theSession.Query<Article>()
                .Where(x => x.AuthorArray.Any(s => favAuthors.Contains(s)))
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(4);
            res[0].Long.ShouldBe(1);
            res[1].Long.ShouldBe(2);
            res[2].Long.ShouldBe(3);
            res[3].Long.ShouldBe(6);
        }

        [Fact]
        public void query_array_with_Intersect_should_blow_up()
        {
            buildAuthorData();

            Exception<BadLinqExpressionException>.ShouldBeThrownBy(() =>
            {
                var res = theSession.Query<Article>()
                    .Where(x => x.AuthorArray.Any(s => favAuthors.Intersect(new Guid[] { Guid.NewGuid() }).Any()))
                    .OrderBy(x => x.Long)
                    .ToList();
            });
        }

        [Fact]
        public void query_guid_list_intersects_array()
        {
            buildAuthorData();

            var res = theSession.Query<Article>()
                .Where(x => x.AuthorList.Any(s => favAuthors.Contains(s)))
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(1);
            res[0].Long.ShouldBe(5);
        }


        [Fact]
        public void query_against_number_array()
        {
            var doc1 = new DocWithArrays { Numbers = new [] { 1, 2, 3 } };
            var doc2 = new DocWithArrays { Numbers = new [] { 3, 4, 5 } };
            var doc3 = new DocWithArrays { Numbers = new [] { 5, 6, 7 } };

            theSession.Store(doc1, doc2, doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Numbers.Contains(3)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

        [Fact]
        #region sample_query_against_string_array
        public void query_against_string_array()
        {
            var doc1 = new DocWithArrays { Strings = new [] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new [] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new [] { "d", "e", "f" } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Strings.Contains("c")).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

        #endregion sample_query_against_string_array

        [Fact]
        public void query_against_string_array_with_Any()
        {
            var doc1 = new DocWithArrays { Strings = new [] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new [] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new [] { "d", "e", "f" } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Strings.Any(_ => _ == "c")).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
        }

        [Fact]
        public void query_against_string_array_with_Length()
        {
            var doc1 = new DocWithArrays { Strings = new [] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new [] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new [] { "d", "e", "f", "g" } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Strings.Length == 4).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc3.Id);
        }

        [Fact]
        public void query_against_string_array_with_Count_method()
        {
            var doc1 = new DocWithArrays { Strings = new [] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new [] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new [] { "d", "e", "f", "g" } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithArrays>().Where(x => x.Strings.Count() == 4).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc3.Id);
        }

        [Fact]
        public void query_against_date_array()
        {
            var doc1 = new DocWithArrays { Dates = new[] { DateTime.Today, DateTime.Today.AddDays(1), DateTime.Today.AddDays(2) } };
            var doc2 = new DocWithArrays { Dates = new[] { DateTime.Today.AddDays(2), DateTime.Today.AddDays(3), DateTime.Today.AddDays(4) } };
            var doc3 = new DocWithArrays { Dates = new[] { DateTime.Today.AddDays(4), DateTime.Today.AddDays(5), DateTime.Today.AddDays(6) } };

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

        #region sample_query_any_string_array
        [Fact]
        public void query_against_number_list_with_any()
        {
            var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7 } };
            var doc4 = new DocWithLists { Numbers = new List<int> { } };

            theSession.Store(doc1, doc2, doc3, doc4);

            theSession.SaveChanges();

            theSession.Query<DocWithLists>().Where(x => x.Numbers.Any(_ => _ == 3)).ToArray()
                .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);

            // Or without any predicate
            theSession.Query<DocWithLists>()
                .Count(x => x.Numbers.Any()).ShouldBe(3);
        }

        #endregion sample_query_any_string_array

        #region sample_query_against_number_list_with_count_method
        [Fact]
        public void query_against_number_list_with_count_method()
        {
            var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7, 8 } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists>()
                .Single(x => x.Numbers.Count() == 4).Id.ShouldBe(doc3.Id);
        }

        #endregion sample_query_against_number_list_with_count_method

        [Fact]
        public void query_against_number_list_with_count_property()
        {
            var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7, 8 } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists>()
                .Single(x => x.Numbers.Count == 4).Id.ShouldBe(doc3.Id);
        }

        [Fact]
        public void query_against_number_list_with_count_property_and_other_operators()
        {
            var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
            var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
            var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7, 8 } };

            theSession.Store(doc1);
            theSession.Store(doc2);
            theSession.Store(doc3);

            theSession.SaveChanges();

            theSession.Query<DocWithLists>()
                .Single(x => x.Numbers.Count > 3).Id.ShouldBe(doc3.Id);
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

        [Fact]
        public void naked_any_hit_without_predicate()
        {
            var targetithchildren = new Target { Number = 1 };
            targetithchildren.Children = new[] { new Target(), };
            var nochildrennullarray = new Target { Number = 2 };
            var nochildrenemptyarray = new Target { Number = 3 };
            nochildrenemptyarray.Children = new Target[] { };
            theSession.Store(nochildrennullarray);
            theSession.Store(nochildrenemptyarray);
            theSession.Store(targetithchildren);
            theSession.SaveChanges();

            var items = theSession.Query<Target>().Where(x => x.Children.Any()).ToList();

            items.Count.ShouldBe(1);
        }
    }

    public class Article
    {
        public Guid Id { get; set; }
        public long Long { get; set; }
        public string[] CategoryArray { get; set; }
        public List<string> CategoryList { get; set; }
        public Guid[] AuthorArray { get; set; }
        public List<Guid> AuthorList { get; set; }
        public Article ReferencedArticle { get; set; }
        public bool Published { get; set; }
    }

    public class DocWithLists
    {
        public Guid Id { get; set; }

        public List<int> Numbers { get; set; }
    }

    public class DocWithLists2
    {
        public Guid Id { get; set; }

        public IList<int> Numbers { get; set; }
    }

    public class DocWithLists3
    {
        public Guid Id { get; set; }

        public IEnumerable<int> Numbers { get; set; }
    }

    public class DocWithArrays
    {
        public Guid Id { get; set; }

        public int[] Numbers { get; set; }

        public string[] Strings { get; set; }

        public DateTime[] Dates { get; set; }
    }
}

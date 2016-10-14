using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;
using System.Collections.Generic;
using System;

namespace Marten.Testing.Linq
{
    public class query_with_intersect_Tests : DocumentSessionFixture<NulloIdentityMap>
    {

        private Guid[] favAuthors = new Guid[] { Guid.NewGuid(), Guid.NewGuid() };

        public query_with_intersect_Tests()
        {
            // test fixtures
            theSession.Store(new Article
            {
                Long = 1,
                CategoryArray = new string[] { "sports", "finance", "health" },
                CategoryList = new List<string> { "sports", "finance", "health" },
                AuthorArray = favAuthors,
                Published = true,
            });

            theSession.Store(new Article
            {
                Long = 2,
                CategoryArray = new string[] { "sports", "astrology" },
                AuthorArray = favAuthors.Take(1).ToArray(),
            });

            theSession.Store(new Article
            {
                Long = 3,
                CategoryArray = new string[] { "health", "finance" },
                CategoryList = new List<string> { "sports", "health" },
                AuthorArray = favAuthors.Skip(1).ToArray(),
            });

            theSession.Store(new Article
            {
                Long = 4,
                CategoryArray = new string[] { "health", "astrology" },
                AuthorList = new List<Guid> { Guid.NewGuid() },
                Published = true,
            });

            theSession.Store(new Article
            {
                Long = 5,
                CategoryArray = new string[] { "sports", "nested" },
                AuthorList = new List<Guid> { Guid.NewGuid(), favAuthors[1] },
            });

            theSession.Store(new Article
            {
                Long = 6,
                AuthorArray = new Guid[] { favAuthors[0], Guid.NewGuid() },
                ReferencedArticle = new Article
                {
                    CategoryArray = new string[] { "nested" },
                }
            });
            theSession.SaveChanges();
        }

        [Fact]
        public void query_string_array_intersects_array()
        {
            var interests = new string[] { "finance", "astrology" };
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
            var interests = new string[] { "health", "astrology" };
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
            var interests = new string[] { "nested" };
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
            var interests = new string[] { "finance", "astrology" };
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
        public void query_guid_list_intersects_array()
        {
            var res = theSession.Query<Article>()
                .Where(x => x.AuthorList.Any(s => favAuthors.Contains(s)))
                .OrderBy(x => x.Long)
                .ToList();

            res.Count.ShouldBe(1);
            res[0].Long.ShouldBe(5);
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

}

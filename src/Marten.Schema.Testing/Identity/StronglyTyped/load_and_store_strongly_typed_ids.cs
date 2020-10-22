using System;
using System.Linq;
using Marten.Schema.Identity.StronglyTyped;
using Marten.Schema.Testing.Identity.StronglyTyped.Model;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Schema.Testing.Identity.StronglyTyped
{
    public class load_and_store_strongly_typed_ids : IntegrationContext
    {
        private readonly ITestOutputHelper _outputHelper;

        public load_and_store_strongly_typed_ids(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public void custom_id_type()
        {
            StoreOptions(s =>
            {
                s.Logger(new TestOutputMartenLogger(this._outputHelper));
                s.UseStronglyTypedId<BlogId>();
            });

            var blog = new Blog(new BlogId(Guid.NewGuid()), "Marten Blog");
            theSession.Store(blog);
            theSession.SaveChanges();

            var loadedBlog = theSession.Load<Blog, BlogId>(blog.Id);
            loadedBlog.ShouldNotBeNull();
            Assert.NotSame(blog, loadedBlog);

            var loadedThroughLinq = theSession.Query<Blog>().Single(b => b.Id == blog.Id);
            loadedThroughLinq.ShouldNotBeNull();
            Assert.NotSame(loadedBlog, loadedThroughLinq);
            Assert.Equal(blog.Id, loadedThroughLinq.Id);
        }

        [Fact]
        public void when_loading_then_a_different_document_should_be_returned()
        {
            StoreOptions(s =>
            {
                s.Logger(new TestOutputMartenLogger(this._outputHelper));
                s.UseStronglyTypedId<BlogId>();
            });

            var blog = new Blog(new BlogId(Guid.NewGuid()), "Marten Blog 2");
            theSession.Store(blog);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                var first = session.Load<Blog, BlogId>(blog.Id);
                var second = session.Load<Blog, BlogId>(blog.Id);
                first.ShouldBeSameAs(second);
            }
        }

        [Fact]
        public void when_filtered_by_foreign_id()
        {
            StoreOptions(s =>
            {
                s.Logger(new TestOutputMartenLogger(this._outputHelper));
                s.UseStronglyTypedId<PostId>();
                s.UseStronglyTypedId<BlogId>();
            });

            var blogId = new BlogId(Guid.NewGuid());
            var firstPost = new Post(new PostId(Guid.NewGuid()), blogId, "First post");
            var secondPost = new Post(new PostId(Guid.NewGuid()), blogId, "Second post");
            var otherPost = new Post(new PostId(Guid.NewGuid()), new BlogId(Guid.NewGuid()), "Other post");
            theSession.Store(firstPost, secondPost, otherPost);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                var posts = session.Query<Post>().Where(p => p.BlogId == blogId).ToArray();
                Assert.Equal(2, posts.Length);
                Assert.All(posts, p => {
                    Assert.Equal(p.BlogId, blogId);
                    });
            }
        }
    }
}

namespace Marten.Schema.Testing.Identity.StronglyTyped.Model
{
    public class Post
    {
        public Post(PostId id, BlogId blogId, string name)
        {
            this.Id = id;
            this.BlogId = blogId;
            this.Name = name;
        }

        public PostId Id { get; }
        public BlogId BlogId { get; }
        public string Name { get; }
    }
}

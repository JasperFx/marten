namespace Marten.Schema.Testing.Identity.StronglyTyped.Model
{
    public class Blog
    {
        public Blog(BlogId id, string name)
        {
            this.Id = id;
            this.Name = name;
        }

        public BlogId Id { get; }

        public string Name { get; }
    }
}

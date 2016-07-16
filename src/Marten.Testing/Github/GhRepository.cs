//using Octokit;

namespace Marten.Testing.Github
{
    public class GhRepository
    {
        public GhRepository()
        {
        }

        /*
        public GhRepository(Octokit.Repository repository)
        {
            Url = repository.Url;
            GitUrl = repository.GitUrl;
            SshUrl = repository.SshUrl;
            Id = repository.Id;
            Owner = repository.Owner;
            Name = repository.Name;
            FullName = repository.FullName;
            Description = repository.Description;
            Homepage = repository.Homepage;
            Language = repository.Language;
            ForksCount = repository.ForksCount;
            StargazersCount = repository.StargazersCount;
        }
        */

        public string Language { get; set; }

        public int StargazersCount { get; set; }

        public int ForksCount { get; set; }

        public string Homepage { get; set; }

        public string Description { get; set; }

        public string FullName { get; set; }

        public string Name { get; set; }

        public User Owner { get; set; }

        public int Id { get; set; }

        public string SshUrl { get; set; }

        public string GitUrl { get; set; }

        public string Url { get; set; }
    }
}
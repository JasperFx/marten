using System.Linq;
using System.Threading.Tasks;
using Octokit;

namespace Marten.Testing.Github
{
    public class RepositoryHistory
    {
        public GhRepository Repository { get; set; }
        public GhUser[] Contributors { get; set; }
        
        public GhCommit[] Commits { get; set; } 
    }




    public static class GithubClientExtensions
    {
        public static RepositoryHistory GetHistory(this GitHubClient client, string owner, string repoName)
        {
            var history = new RepositoryHistory();

            var repo = client.Repository.Get(owner, repoName).ContinueWith(t =>
            {
                history.Repository = new GhRepository(t.Result);
            });

            var contributors = client.Repository.GetAllContributors(owner, repoName).ContinueWith(t =>
            {
                history.Contributors = t.Result.Select(x => new GhUser(x)).ToArray();
            });

            var lookups = client.Repository.Commits.GetAll(owner, repoName).ContinueWith(t =>
            {
                return t.Result.Take(500).Select(c =>
                {
                    return client.Repository.Commits.Get(owner, repoName, c.Sha);
                });
            });

            Task.WaitAll(repo, contributors, lookups);
            var results = lookups.Result.ToArray();
            Task.WaitAll(results);

            history.Commits = results.Select(x => new GhCommit(x.Result)).ToArray();

            return history;
        }
    }
}
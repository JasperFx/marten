using System.Diagnostics;
using System.Threading.Tasks;
using CodeTracker;
//using Octokit;

namespace Marten.Testing.AsyncDaemon
{
    /*
    public class GithubDataRecorder
    {
        private readonly GitHubClient _client;
        private readonly string _directory;

        public GithubDataRecorder(Credentials credentials, string directory)
        {
            _directory = directory;
            _client = new GitHubClient(new ProductHeaderValue("marten-testing")) {Credentials = credentials};
        }

        public async Task RecordProject(string organization, string projectName)
        {
            Debug.WriteLine($"Starting to fetch {organization}/{projectName}");

            var repository = await _client.Repository.Get(organization, projectName).ConfigureAwait(false);

            var project = new GithubProject(organization, projectName, repository.CreatedAt);

            var issues = await _client.Issue.GetAllForRepository(organization, projectName).ConfigureAwait(false);

            foreach (var issue in issues)
            {
                project.RecordIssue(issue);
            }

            Debug.WriteLine($"Done with issues for {organization}/{projectName}");


            var commits = await _client.Repository.Commit.GetAll(organization, projectName, new ApiOptions {PageSize = 100, PageCount = 10}).ConfigureAwait(false);

            foreach (var commit in commits)
            {
                var full =
                    await _client.Repository.Commit.Get(organization, projectName, commit.Sha).ConfigureAwait(false);

                project.RecordCommit(commit, full.Stats);
            }

            project.SaveTo(_directory);

            Debug.WriteLine($"Persisted {organization}/{projectName}");
        }


    }
    */
}
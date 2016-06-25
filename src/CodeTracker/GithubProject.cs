using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Newtonsoft.Json;
using Octokit;
using FileMode = System.IO.FileMode;

namespace CodeTracker
{
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


    public class GithubProject
    {
        private static readonly Random _random = new Random();

        private readonly IList<Timestamped> _events = new List<Timestamped>();


        public GithubProject()
        {
        }

        public GithubProject(string organizationName, string projectName, DateTimeOffset createdAt)
        {
            OrganizationName = organizationName;
            ProjectName = projectName;

            _events.Add(new ProjectStarted
            {
                Name = projectName,
                Organization = organizationName,
                Timestamp = createdAt
            });
        }


        public string OrganizationName { get; set; }
        public string ProjectName { get; set; }

        public Timestamped[] Events
        {
            get { return _events.ToArray(); }
            set
            {
                _events.Clear();
                _events.AddRange(value);
            }
        }

        public void RecordIssue(Issue issue)
        {
            _events.Add(new IssueCreated
            {
                Description = issue.Title,
                UserName = issue.User.Login,
                Timestamp = issue.CreatedAt,
                Number = issue.Number
            });

            if (issue.State == ItemState.Closed)
            {
                _events.Add(new IssueClosed
                {
                    UserName = issue.User.Login,
                    Timestamp = issue.ClosedAt.Value,
                    Number = issue.Number
                });


                if (_random.Next(0, 10) > 8)
                {
                    _events.Add(new IssueReopened
                    {
                        UserName = issue.User.Login,
                        Timestamp = issue.ClosedAt.Value.AddDays(1),
                        Number = issue.Number
                    });
                }
            }
        }

        public void SaveTo(string directory)
        {
            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.All
            };

            var file = directory.AppendPath(ProjectName + ".json");

            using (var stream = new FileStream(file, FileMode.Create))
            {
                serializer.Serialize(new JsonTextWriter(new StreamWriter(stream)), this);
            }
        }

        public void RecordCommit(GitHubCommit commit, GitHubCommitStats stats)
        {
            var @event = new Commit
            {
                Sha = commit.Sha,
                Message = commit.Commit.Message,
                Timestamp = commit.Commit.Author.Date,
                Additions = stats.Additions,
                Deletions = stats.Deletions
            };

            _events.Add(@event);
        }
    }
}
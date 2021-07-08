using System;
using System.Threading.Tasks;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreWithMarten
{
    public class Issue
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public bool Open { get; set; }
    }

    public class CreateIssue
    {
        public string Description { get; set; }
    }

    public class IssueCreated
    {
        public Guid IssueId { get; set; }
    }

    public class IssueCreator
    {
        private readonly IDocumentSession _session;

        public IssueCreator(IDocumentSession session)
        {
            _session = session;
        }

        public Task Insert(Issue issue)
        {
            _session.Store(issue);

            // This does not do what you want it to
            // do here
            try
            {
                return _session.SaveChangesAsync();
            }
            catch (Exception e)
            {
                // handle the exception
                return Task.CompletedTask;
            }
        }
    }



    public class IssueController
    {
        [HttpPost("/issue")]
        public async Task<IssueCreated> PostIssue(
            [FromBody] CreateIssue command,
            [FromServices] IDocumentSession session)
        {
            var issue = new Issue
            {
                Description = command.Description
            };

            session.Store(issue);
            await session.SaveChangesAsync();

            return new IssueCreated
            {
                IssueId = issue.Id
            };
        }

        [HttpPost("/issue")]
        public IssueCreated PostIssueSync(
            [FromBody] CreateIssue command,
            [FromServices] IssueCreator creator)
        {
            var issue = new Issue
            {
                Description = command.Description
            };

            // Better, but still not ideal
            creator.Insert(issue)
                .GetAwaiter().GetResult();

            return new IssueCreated
            {
                IssueId = issue.Id
            };
        }

        public class RaceCondition
        {
            private readonly IDocumentSession _session;

            public RaceCondition(IDocumentSession session)
            {
                _session = session;
            }

            public async Task WorkOnIssue(Guid issueId)
            {
                // This will work just fine
                var lookup = _session.LoadAsync<Issue>(issueId);
                var issue = await lookup;
            }
        }

        public interface IValidator
        {
            Task AssertValid(Issue issue);
            Task<string> Validate(Issue issue);
        }

        public class Validator: IValidator
        {
            public Task AssertValid(Issue issue)
            {
                // throw if there are any errors
                return Task.CompletedTask;
            }

            public Task<string> Validate(Issue issue)
            {
                return Task.FromResult("No problems");
            }
        }
    }
}

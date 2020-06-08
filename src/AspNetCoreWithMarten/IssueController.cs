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
    }
}

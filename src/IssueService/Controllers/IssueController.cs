using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace IssueService.Controllers
{
    public class Issue
    {
        private static Random _random = new Random();

        public static Issue Random()
        {
            return new Issue {Description = Guid.NewGuid().ToString(), Open = _random.Next(0, 10) > 7};
        }

        public Guid Id { get; set; }
        public string Description { get; set; }
        public bool Open { get; set; }
    }


    public class GetIssueController: ControllerBase
    {
        private readonly IQuerySession _session;

        public GetIssueController(IQuerySession session)
        {
            _session = session;
        }

        [HttpGet("/issue/{issueId}")]
        public Task Get(Guid issueId)
        {
            return _session.Json.WriteToHttpById<Issue>(issueId, HttpContext);
        }

        [HttpGet("/issue2/{issueId}")]
        public Task Get2(Guid issueId)
        {
            return _session.Query<Issue>().Where(x => x.Id == issueId)
                .WriteJsonDocumentToHttp(HttpContext);
        }


        [HttpGet("/issue/open")]
        public Task OpenIssues()
        {
            return _session.Query<Issue>().Where(x => x.Open).WriteJsonArrayToHttp(HttpContext);
        }
    }

}

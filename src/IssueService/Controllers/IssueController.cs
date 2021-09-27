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
            // This "streams" the raw JSON to the HttpResponse
            // w/o ever having to read the full JSON string or
            // deserialize/serialize within the HTTP request
            return _session.Json
                .WriteById<Issue>(issueId, HttpContext);
        }

        [HttpGet("/issue2/{issueId}")]
        public Task Get2(Guid issueId)
        {
            return _session.Query<Issue>().Where(x => x.Id == issueId)
                .WriteSingle(HttpContext);
        }


        [HttpGet("/issue/open")]
        public Task OpenIssues()
        {
            // This "streams" the raw JSON to the HttpResponse
            // w/o ever having to read the full JSON string or
            // deserialize/serialize within the HTTP request
            return _session.Query<Issue>()
                .Where(x => x.Open)
                .WriteArray(HttpContext);
        }
    }

}

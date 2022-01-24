using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Examples
{


    // ICompiledListQuery<T> is from Marten
    public class OpenIssuesAssignedToUser: ICompiledListQuery<Issue>
    {
        public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
        {
            return q => q
                .Where(x => x.AssigneeId == UserId)
                .Where(x => x.Status == "Open");
        }

        public Guid UserId { get; set; }
    }

    public class samples
    {
        private readonly ITestOutputHelper _output;

        public static async Task using_it(Guid userId)
        {
    var store = DocumentStore.For(opts =>
    {
        opts.Connection("some connection string");
    });

    await using var session = store.QuerySession();

    var issues = await session.Query<Issue>()
        .Where(x => x.AssigneeId == userId)
        .Where(x => x.Status == "Open")
        .ToListAsync();

    // do whatever with the issues
        }

        public samples(ITestOutputHelper output)
        {
            _output = output;

        }

        [Fact]
        public void write_code()
        {
            var store = DocumentStore.For(ConnectionSource.ConnectionString);
            var code = store.Advanced
                .SourceCodeForCompiledQuery(typeof(OpenIssuesAssignedToUser));
            _output.WriteLine(code);
        }
    }

    public class FindByName: ICompiledListQuery<User>
    {
        public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
        {
            return q => q.Where(x => x.UserName == UserName || (x.FirstName == FirstName && x.LastName == LastName));
        }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
    }

    public class BadIssues: ICompiledListQuery<Issue>
    {
        public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
        {
            return q => q.Include(x => x.AssigneeId, Users)
                .Include(x => x.ReporterId, Reported)
                .Where(x => x.Title.Contains("Bad"));
        }

        public IList<User> Users { get; } = new List<User>();
        public IDictionary<Guid, User> Reported = new Dictionary<Guid, User>();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Internals.Compiled
{
    public class compiled_query_generation_smoke_tests
    {
        protected CompiledQueryPlan planFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            using var store = DocumentStore.For(ConnectionSource.ConnectionString);
            using var session = store.QuerySession();

            return QueryCompiler.BuildPlan((QuerySession)session, query, new StoreOptions());
        }

        protected ICompiledQuerySource buildQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            using var store = DocumentStore.For(ConnectionSource.ConnectionString);
            store.Advanced.Clean.CompletelyRemoveAll();

            using var session = store.QuerySession();

            var plan = QueryCompiler.BuildPlan((QuerySession) session, query, new StoreOptions());

            return new CompiledQuerySourceBuilder(plan, new StoreOptions()).Build();
        }

        //[Fact]
        public void build_simple_plan_with_one_string_argument()
        {
            var plan = planFor(new FindByStringValue());

            var parameter = plan.Parameters.Single();
            parameter.Member.Name.ShouldBe(nameof(FindByStringValue.StringValue));
        }

        //[Fact]
        public void build_source_for_simple_compiled_query()
        {
            buildQuerySourceFor(new FindByStringValue());
        }

        //[Fact]
        public void multiple_strings()
        {
            var plan = planFor(new FindByName());


            plan.Parameters.Count.ShouldBe(3);

            plan.CorrectedCommandText().ShouldBe("select d.data, d.id, d.mt_version from public.mt_doc_user as d where (d.data ->> 'UserName' = ? or (d.data ->> 'FirstName' = ? and d.data ->> 'LastName' = ?))");
        }


        //[Fact]
        public void finds_query_statistics_property()
        {
            var plan = planFor(new FindByStringValue());

            plan.StatisticsMember.Name.ShouldBe("Statistics");
        }

        //[Fact]
        public void build_source_for_query_statistics()
        {
            var source = buildQuerySourceFor(new FindByStringValue());
            source.ShouldNotBeNull();
        }

        //[Fact]
        public void try_to_process_includes()
        {
            var plan = planFor(new BadIssues());

            plan.IncludePlans.Count.ShouldBe(2);
            plan.IncludeMembers.Count.ShouldBe(2);
        }

        //[Fact]
        public void build_source_for_includes()
        {
            var source = buildQuerySourceFor(new BadIssues());
            source.ShouldNotBeNull();
        }

    }

    public class FindByStringValue: ICompiledListQuery<Target>
    {
        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return q => q.Where(x => x.String == StringValue);
        }

        public string StringValue { get; set; }

        public QueryStatistics Statistics { get; } = new QueryStatistics();
    }

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

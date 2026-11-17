using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LinqTests.Operators;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;

namespace LinqTests.Acceptance;

// #5916: ToCommand(fetchType) -- and ExplainAsync(fetchType), which explains that same command -- has to
// show the SQL that executing the query with the matching operator actually sends. For a join it used to
// count, or test for existence of, the outer side only, because the preview applied Count/Any to the outer
// statement itself while execution wraps the joined result in a CTE first.
//
// Every shape here is previewed with each FetchType and then executed with each async operator that the
// FetchType stands for, and the two commands are compared: the SQL text, and the value of every parameter
// in the position it is referenced from. ToCommand() renders `:p0` placeholders into one command while
// execution sends `$1` placeholders in a batch, so both are canonicalized by replacing each placeholder
// with the value it binds before comparing.
public class preview_command_matches_the_executed_command: OneOffConfigurationsContext
{
    private async Task SetupData()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<JoinCustomer>();
            opts.Schema.For<JoinOrder>();
            opts.Schema.For<Target>();
            opts.Schema.For<Issue>();
            opts.Schema.For<User>();
        });

        var alice = new JoinCustomer { Id = Guid.NewGuid(), Name = "Alice", City = "Seattle" };
        var bob = new JoinCustomer { Id = Guid.NewGuid(), Name = "Bob", City = "Portland" };
        var charlie = new JoinCustomer { Id = Guid.NewGuid(), Name = "Charlie", City = "Seattle" };

        theSession.Store(alice, bob, charlie);
        theSession.Store(
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = alice.Id, Status = "Active", Amount = 100m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = alice.Id, Status = "Shipped", Amount = 200m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = bob.Id, Status = "Active", Amount = 300m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = "Orphaned", Amount = 400m });

        var targets = Enumerable.Range(1, 5).Select(i => new Target
        {
            Number = i,
            Color = i % 2 == 0 ? Colors.Blue : Colors.Green,
            NumberArray = [i, 3],
            StringArray = ["a", "b" + i],
            Children = [new Target { Number = i }, new Target { Number = i + 10 }]
        }).ToArray();
        theSession.Store(targets);

        var user = new User { UserName = "assignee" };
        theSession.Store(user);
        theSession.Store(new Issue { Title = "One", Number = 1, AssigneeId = user.Id },
            new Issue { Title = "Two", Number = 2, AssigneeId = user.Id });

        await theSession.SaveChangesAsync();
    }

    #region join shapes

    [Fact]
    public async Task inner_join()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .Join(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount }));
    }

    [Fact]
    public async Task inner_join_filtered_after_the_join()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .Join(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount })
            .Where(x => x.Amount > 150m));
    }

    [Fact]
    public async Task join_with_a_where_on_the_inner_side()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .Where(c => c.City == "Seattle")
            .Join(s.Query<JoinOrder>().Where(o => o.Status == "Active"), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, o.Amount }));
    }

    [Fact]
    public async Task left_join()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .LeftJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, Amount = (decimal?)o.Amount }));
    }

    [Fact]
    public async Task right_join()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .RightJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount }));
    }

    [Fact]
    public async Task hand_written_group_join_and_select_many()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .GroupJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
            .SelectMany(x => x.orders, (x, o) => new { x.c.Name, o.Amount }));
    }

    [Fact]
    public async Task hand_written_group_join_and_select_many_with_default_if_empty()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<JoinCustomer>()
            .GroupJoin(s.Query<JoinOrder>().Where(o => o.Amount > 150m), c => c.Id, o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(x => x.orders.DefaultIfEmpty(), (x, o) => new { x.c.Name, Amount = (decimal?)o.Amount }));
    }

    #endregion

    #region other shapes

    [Fact]
    public async Task plain_where()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().Where(x => x.Number > 2));
    }

    [Fact]
    public async Task select_projection()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>()
            .Where(x => x.Number > 1)
            .Select(x => new { x.Id, x.Number }));
    }

    [Fact]
    public async Task include()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Issue>()
            .Where(x => x.Number > 0)
            .Include(x => x.AssigneeId, new List<User>()));
    }

    [Fact]
    public async Task distinct()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().Select(x => x.Color).Distinct());
    }

    [Fact]
    public async Task child_collection_any()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().Where(x => x.Children.Any(c => c.Number > 12)));
    }

    [Fact]
    public async Task child_collection_contains()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().Where(x => x.NumberArray.Contains(3)));
    }

    [Fact]
    public async Task order_by_and_take()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().OrderBy(x => x.Number).Take(3));
    }

    [Fact]
    public async Task order_by_skip_and_take()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().OrderByDescending(x => x.Number).Skip(1).Take(2));
    }

    [Fact]
    public async Task select_many_over_a_child_document_collection()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().SelectMany(x => x.Children));
    }

    [Fact]
    public async Task select_many_over_a_child_value_collection()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>().SelectMany(x => x.StringArray));
    }

    [Fact]
    public async Task group_by()
    {
        await SetupData();

        await previewShouldMatchExecution(s => s.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new { g.Key, Count = g.Count() }));
    }

    #endregion

    private async Task previewShouldMatchExecution<T>(Func<IQuerySession, IQueryable<T>> query)
        where T : notnull
    {
        var mismatches = new List<string>();

        await compare(mismatches, query, FetchType.FetchMany, "ToListAsync()", q => q.ToListAsync());
        await compare(mismatches, query, FetchType.Count, "CountAsync()", q => q.CountAsync());
        await compare(mismatches, query, FetchType.Count, "LongCountAsync()", q => q.LongCountAsync());
        await compare(mismatches, query, FetchType.Any, "AnyAsync()", q => q.AnyAsync());
        await compare(mismatches, query, FetchType.FetchOne, "FirstAsync()", q => q.FirstAsync());
        await compare(mismatches, query, FetchType.FetchOne, "FirstOrDefaultAsync()", q => q.FirstOrDefaultAsync());

        if (mismatches.Count != 0)
        {
            throw new ShouldAssertException(string.Join(Environment.NewLine + Environment.NewLine, mismatches));
        }
    }

    private async Task compare<T>(List<string> mismatches, Func<IQuerySession, IQueryable<T>> query,
        FetchType fetchType, string operatorName, Func<IQueryable<T>, Task> execute) where T : notnull
    {
        // Fresh sessions on both sides, so the CTE and temp table names -- numbered per session -- line up
        string previewed;
        await using (var session = theStore.QuerySession())
        {
            previewed = canonical(query(session).ToCommand(fetchType));
        }

        await using var executing = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        executing.Logger = recorder;

        await execute(query(executing));

        var executed = canonical(recorder.Commands);

        if (previewed != executed)
        {
            mismatches.Add($"ToCommand(FetchType.{fetchType}) is not what {operatorName} executed"
                           + $"{Environment.NewLine}  previewed: {previewed}"
                           + $"{Environment.NewLine}  executed:  {executed}");
        }
    }

    private static readonly Regex PreviewPlaceholder = new(@"(?<![:\w]):(p\d+)\b", RegexOptions.Compiled);
    private static readonly Regex BatchPlaceholder = new(@"\$(\d+)\b", RegexOptions.Compiled);

    // ToCommand() is one command with named :pN parameters
    private static string canonical(NpgsqlCommand command)
    {
        return PreviewPlaceholder.Replace(command.CommandText,
            m => render(ExecutedCommandRecorder.ParameterValue(command.Parameters[m.Groups[1].Value])));
    }

    // Execution is a batch, each command with its own positional $N parameters. The preview's single
    // command is the same statements back to back, so the batch commands are concatenated the same way.
    private static string canonical(IEnumerable<(string Sql, object[] Parameters)> commands)
    {
        return string.Concat(commands.Select(c =>
            BatchPlaceholder.Replace(c.Sql, m => render(c.Parameters[int.Parse(m.Groups[1].Value) - 1]))));
    }

    private static string render(object value)
    {
        var text = value switch
        {
            null or DBNull => "null",
            string s => "'" + s + "'",
            IEnumerable sequence => "[" + string.Join(",", sequence.Cast<object>().Select(render)) + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

        return $"{{{value?.GetType().Name}:{text}}}";
    }
}

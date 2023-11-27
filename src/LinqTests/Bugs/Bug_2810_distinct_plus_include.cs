using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2810_distinct_plus_include : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2810_distinct_plus_include(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task do_not_blow_up()
    {
        var exportId = 34;
        var includedTableDocuments = new List<IncludedTable>();
        var skip = 30;
        var take = 10;

        theSession.Logger = new TestOutputMartenLogger(_output);

        var results = await theSession.Query<MainTable>()
            .Include(x => x.IncludedTableId, includedTableDocuments)
            .Where(i => i.ExportId == exportId)
            .OrderBy(i => i.IncludedTableId)
            .Select(i => i.IncludedTableId)
            .Distinct()
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}

public class MainTable
{
    [Identity]
    public string Id { get; set; }
    public string IncludedTableId { get; set; }
    public int ExportId { get; set; }
}

public class IncludedTable
{
    public string Id { get; set; }
    public string OtherProperty { get; set; }
}

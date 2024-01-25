using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2916_ngram_index_when_multi_tenanted : BugIntegrationContext
{
    [Fact]
    public async Task can_create_index()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<NGramDoc>()
                .NgramIndex(x => x.NGramString)
                .MultiTenanted();
        });

        theSession.Store(new NGramDoc
        {
            SomeList = ["foo", "bar"],
            NGramString = "something searchable"
        });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<NGramDoc>()
            .Where(x => x.NGramString.NgramSearch("some") && x.SomeList.Any(y => y.StartsWith("fo")))
            .ToListAsync();

        Console.WriteLine("Results: " + result.Count);
    }
}

public class NGramDoc
{
    public Guid Id { get; set; }
    public List<string> SomeList { get; set; } = new();
    public string NGramString { get; set; } = "";
}

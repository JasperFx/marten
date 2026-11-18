using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Json;

public class JsonTicket
{
    public Guid Id { get; set; }
    public string Title { get; set; }
}

public class JsonTicketById: ICompiledQuery<JsonTicket, JsonTicket>
{
    public Guid Id { get; set; }

    public System.Linq.Expressions.Expression<Func<IMartenQueryable<JsonTicket>, JsonTicket>> QueryIs()
        => q => q.FirstOrDefault(x => x.Id == Id);
}

public class JsonTicketsByTitle: ICompiledListQuery<JsonTicket>
{
    public string Title { get; set; }

    public System.Linq.Expressions.Expression<Func<IMartenQueryable<JsonTicket>, System.Collections.Generic.IEnumerable<JsonTicket>>> QueryIs()
        => q => q.Where(x => x.Title == Title);
}

/// <summary>
///     A compiled query streamed as JSON creates its document table first, as every other read does. It used to run
///     straight against the table, so the first such read of a type nothing had touched yet failed with 42P01
///     (Marten.AspNetCore's WriteOne / StreamOne returned 500 instead of 404).
/// </summary>
public class compiled_json_queries_on_a_new_table: OneOffConfigurationsContext
{
    [Fact]
    public async Task streaming_one_document_finds_nothing_instead_of_failing()
    {
        await using var query = theStore.QuerySession();
        var stream = new MemoryStream();

        (await query.StreamJsonOne(new JsonTicketById { Id = Guid.NewGuid() }, stream)).ShouldBeFalse();
    }

    [Fact]
    public async Task streaming_many_documents_finds_none_instead_of_failing()
    {
        await using var query = theStore.QuerySession();
        var stream = new MemoryStream();

        (await query.StreamJsonMany(new JsonTicketsByTitle { Title = "nothing" }, stream)).ShouldBe(0);
    }
}

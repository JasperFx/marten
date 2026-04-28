using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_4282_is_one_of_against_string_list: OneOffConfigurationsContext
{
    [Fact]
    public async Task can_query_string_list_with_is_one_of_against_runtime_list()
    {
        var doc1 = new Issue4282Target { RelatedIds = ["related-1", "related-2"] };
        var doc2 = new Issue4282Target { RelatedIds = ["related-3"] };
        var doc3 = new Issue4282Target { RelatedIds = ["related-4", "related-5"] };

        using var session = theStore.LightweightSession();
        session.Store(doc1, doc2, doc3);
        await session.SaveChangesAsync();

        IList<string> relatedIds = ["related-2", "related-4", "unknown-related"];

        var ids = await session.Query<Issue4282Target>()
            .Where(x => x.RelatedIds.IsOneOf(relatedIds))
            .Select(x => x.Id)
            .ToListAsync();

        // doc1 (matches "related-2") and doc3 (matches "related-4") are
        // expected; doc2 should not match. Use set-membership rather than a
        // sequential ShouldHaveTheSameElementsAs because the Ids are
        // server-generated Guids and don't sort in declaration order.
        ids.Count.ShouldBe(2);
        ids.ShouldContain(doc1.Id);
        ids.ShouldContain(doc3.Id);

        var notIds = await session.Query<Issue4282Target>()
            .Where(x => !x.RelatedIds.IsOneOf(relatedIds))
            .Select(x => x.Id)
            .ToListAsync();

        notIds.Count.ShouldBe(1);
        notIds.ShouldContain(doc2.Id);
    }

    public class Issue4282Target
    {
        public Guid Id { get; set; }
        public List<string> RelatedIds { get; set; } = [];
    }
}

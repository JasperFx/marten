#nullable enable
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;

namespace LinqTests.Unions.Samples;

// Compile-only samples for docs/documents/querying/linq/unions.md (net11.0 only, like every file under
// Unions/). Nothing here runs as a test; each shape is covered by union_member_linq_tests.cs.

#region sample_union_document_types
public record Card(string Last4);

public record Cash(decimal Amount);

// Both cases are JSON objects, so System.Text.Json needs a classifier to read the union back
[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union Payment(Card, Cash);

public class Invoice
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Payment Payment { get; set; }
}
#endregion

public class union_documentation_samples
{
    #region sample_querying_union_members
    public async Task querying_union_members(IQuerySession session)
    {
        // Which case the union holds. Reach the case through .Value:
        // `x.Payment is Card` does not compile in an expression tree
        var byCard = await session.Query<Invoice>().Where(x => x.Payment.Value is Card).ToListAsync();

        // A member of one case. Rows holding the other case never match
        var card1234 = await session.Query<Invoice>()
            .Where(x => ((Card)x.Payment.Value!).Last4 == "1234")
            .ToListAsync();

        var bigCash = await session.Query<Invoice>()
            .Where(x => (x.Payment.Value as Cash)!.Amount > 50m)
            .ToListAsync();

        // The default union holds no case
        var unpaid = await session.Query<Invoice>().Where(x => x.Payment.Value == null).ToListAsync();

        // Order by, and project, a member of a case
        var cashByAmount = await session.Query<Invoice>()
            .Where(x => x.Payment.Value is Cash)
            .OrderByDescending(x => ((Cash)x.Payment.Value!).Amount)
            .Select(x => x.Name)
            .ToListAsync();
    }
    #endregion
}

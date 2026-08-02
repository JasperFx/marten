using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Tags;

namespace JasperFx.Events.ComplianceTests;

/// <summary>
/// The batched DCB query surface, which is the one part of the seed suites with no JasperFx
/// abstraction today: Marten exposes it as <c>batch.Events.EventsExist(query)</c> through an
/// <c>IBatchEvents</c> sub-object, Polecat flat on its own batch type. Method names and signatures
/// already match exactly — only the accessor path differs.
/// </summary>
/// <remarks>
/// As with the products' own batched queries, the tasks returned here do not complete until
/// <see cref="Execute"/> has run.
/// </remarks>
public interface IComplianceBatch
{
    Task<bool> EventsExist(EventTagQuery query);

    Task<IEventBoundary<T>> FetchForWritingByTags<T>(EventTagQuery query) where T : class;

    Task Execute(CancellationToken token = default);
}

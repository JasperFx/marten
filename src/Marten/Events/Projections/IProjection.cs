using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Projections
{
    /// <summary>
    /// Interface for all event projections
    /// </summary>
    public interface IProjection
    {
        /// <summary>
        /// Apply inline projections during synchronous operations
        /// </summary>
        /// <param name="operations"></param>
        /// <param name="streams"></param>
        void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams);

        /// <summary>
        /// Apply inline projections during asynchronous operations
        /// </summary>
        /// <param name="operations"></param>
        /// <param name="streams"></param>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation);
    }

}

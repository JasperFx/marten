using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Storage;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Marks a projection as producing a single type of Marten document
    /// </summary>
    public interface IDocumentProjection : IProjection
    {
        Type Produces { get; }
    }

    public abstract class DocumentProjection<T>
    {
        public Type Produces => typeof(T);        
        public void EnsureStorageExists(ITenant tenant)
        {            
            tenant.EnsureStorageExists(Produces);
        }
    }
}
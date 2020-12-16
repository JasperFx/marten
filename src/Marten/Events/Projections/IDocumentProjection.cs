using System;
using Marten.Storage;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Marks a projection as producing a single type of Marten document
    /// </summary>
    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public interface IDocumentProjection: IProjection
    {
        Type Produces { get; }
    }


    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public abstract class DocumentProjection<T>
    {
        public Type Produces => typeof(T);

        public void EnsureStorageExists(ITenant tenant)
        {
            tenant.EnsureStorageExists(Produces);
        }
    }
}

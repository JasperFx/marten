using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Storage;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public interface IDocumentsProjection: IProjection
    {
        Type[] Produces { get; }
    }

}

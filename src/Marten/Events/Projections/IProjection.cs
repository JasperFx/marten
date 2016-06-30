using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;

namespace Marten.Events.Projections
{
    public interface IProjection
    {
        void Apply(IDocumentSession session, EventStream[] streams);
        Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token);

        Type[] Consumes { get; }
        Type Produces { get; }

        AsyncOptions AsyncOptions { get; }
    }
}
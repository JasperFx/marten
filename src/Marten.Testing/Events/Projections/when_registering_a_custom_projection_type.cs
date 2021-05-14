using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Projections
{
    public class when_registering_a_custom_projection_type: IDisposable
    {
        private readonly DocumentStore _store;
        private ProjectionSource theProjection;

        public when_registering_a_custom_projection_type()
        {
            _store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.Projections.Add(new MyProjection(), ProjectionLifecycle.Async,
                    projectionName: "NewProjection", asyncConfiguration:
                    o =>
                    {
                        o.BatchSize = 111;
                    });
            });

            _store.Options.Projections.TryFindProjection("NewProjection", out theProjection)
                .ShouldBeTrue();
        }

        [Fact]
        public void can_customize_the_projection_name()
        {
            theProjection.ProjectionName.ShouldBe("NewProjection");
        }

        [Fact]
        public void can_customize_the_async_options()
        {
            theProjection.Options.BatchSize.ShouldBe(111);
        }

        [Fact]
        public void can_customize_the_projection_lifecycle()
        {
            theProjection.Lifecycle.ShouldBe(ProjectionLifecycle.Async);
        }

        public void Dispose()
        {
            _store?.Dispose();
        }

        public class MyProjection: IProjection
        {
            public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
            {
                throw new System.NotImplementedException();
            }

            public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
            {
                throw new System.NotImplementedException();
            }
        }
    }
}

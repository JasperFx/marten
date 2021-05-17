using System;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class cannot_register_duplicate_projections_by_name
    {
        [Fact]
        public void cannot_register_duplicate_projection_names()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                DocumentStore.For(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.Projections.Add<Projection1>();
                    opts.Projections.Add<Projection2>();
                });
            });
        }

        public class Projection1: EventProjection
        {
            public Projection1()
            {
                ProjectionName = "Same";
            }

            public AEvent Create(BEvent travel, IEvent e)
            {
                return new AEvent();
            }
        }

        public class Projection2: EventProjection
        {
            public Projection2()
            {
                ProjectionName = "Same";
            }

            public AEvent Create(BEvent travel, IEvent e)
            {
                return new AEvent();
            }
        }



    }
}

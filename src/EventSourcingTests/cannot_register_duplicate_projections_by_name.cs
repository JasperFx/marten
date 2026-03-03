using System;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public partial class cannot_register_duplicate_projections_by_name
{
    [Fact]
    public void cannot_register_duplicate_projection_names()
    {
        Should.Throw<DuplicateSubscriptionNamesException>(() =>
        {
            DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.Projections.Add<Projection1>(ProjectionLifecycle.Inline);
                opts.Projections.Add<Projection2>(ProjectionLifecycle.Inline);
            });
        });
    }

    public partial class Projection1: EventProjection
    {
        public Projection1()
        {
            Name = "Same";
        }

        public AEvent Create(BEvent travel, IEvent e)
        {
            return new AEvent();
        }
    }

    public partial class Projection2: EventProjection
    {
        public Projection2()
        {
            Name = "Same";
        }

        public AEvent Create(BEvent travel, IEvent e)
        {
            return new AEvent();
        }
    }
}

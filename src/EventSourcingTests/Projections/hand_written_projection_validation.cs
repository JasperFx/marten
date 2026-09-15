using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

/// <summary>
///     ⚠️ <b>A release-note item rather than a bug fix, and the reason it belongs in a test.</b> A
///     hand-written <see cref="IProjection" /> is registered through a <c>ProjectionWrapper</c>, and
///     <c>ProjectionGraph.AssertValidity</c> used to run <c>OfType&lt;IValidatedProjection&lt;T&gt;&gt;()</c>
///     over the wrappers — which are not the projection the user wrote. So a hand-written projection
///     that also implemented <c>IValidatedProjection&lt;StoreOptions&gt;</c> was never asked to validate
///     anything, and its checks passed by never running (jasperfx#845).
///     <para>
///         JasperFx 2.70.0 unwraps first. The consequence for a Marten application is that
///         configuration errors which had been silently passing now surface when the store is built —
///         which is what the check was written for, but it is a new failure at an old call site.
///     </para>
/// </summary>
public class hand_written_projection_validation
{
    [Fact]
    public void a_hand_written_projection_is_now_asked_to_validate()
    {
        var projection = new ValidatingHandWrittenProjection();

        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "hand_written_validation";
                opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            });
        });

        ex.Message.ShouldContain("this projection is misconfigured");
        projection.WasAsked.ShouldBeTrue();
    }

    [Fact]
    public void a_hand_written_projection_with_nothing_to_report_still_builds()
    {
        var projection = new ValidatingHandWrittenProjection { Complaints = [] };

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "hand_written_validation";
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
        });

        projection.WasAsked.ShouldBeTrue();
    }
}

public class ValidatingHandWrittenProjection: IProjection, IValidatedProjection<StoreOptions>
{
    public bool WasAsked { get; private set; }

    public string[] Complaints { get; set; } = ["this projection is misconfigured"];

    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        WasAsked = true;
        return Complaints;
    }

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation) => Task.CompletedTask;

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation) => Task.CompletedTask;
}

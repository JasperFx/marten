using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_custom_projection_configuration : OneOffConfigurationsContext
{
    [Fact]
    public void does_call_custom_projection_configuration()
    {
        var configureProjection = Substitute.For<Action<SingleStreamProjection<TodoAggregate>>>();

        StoreOptions(_ =>
        {
            _.Projections.Snapshot<TodoAggregate>(ProjectionLifecycle.Inline, configureProjection);
        });

        configureProjection.Received(1).Invoke(Arg.Any<SingleStreamProjection<TodoAggregate>>());
    }

    [Fact]
    public async Task does_delete_document_upon_deleted_event()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<TodoAggregate>(ProjectionLifecycle.Inline, configureProjection: p =>
            {
                p.DeleteEvent<TodoDeleted>();
            });
        });

        var todoId = Guid.NewGuid();
        theSession.Events.StartStream<TodoAggregate>(todoId, new TodoCreated(todoId, "Write code"));
        await theSession.SaveChangesAsync();

        // Make sure the document has been created
        (await theSession.LoadAsync<TodoAggregate>(todoId)).ShouldNotBeNull();

        // Append the delete
        theSession.Events.Append(todoId, new TodoDeleted(todoId));
        await theSession.SaveChangesAsync();

        // Make sure the document now has been deleted
        (await theSession.LoadAsync<TodoAggregate>(todoId)).ShouldBeNull();
    }
}

public record TodoCreated(Guid Id, string Task);
public record TodoDeleted(Guid Id);

public class TodoAggregate
{
    public Guid Id { get; set; }
    public string Task { get; set; }

    public static TodoAggregate Create(TodoCreated created) => new()
    {
        Task = created.Task
    };
}

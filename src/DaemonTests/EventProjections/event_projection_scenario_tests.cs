using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Events.TestSupport;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.EventProjections;

public class event_projection_scenario_tests : OneOffConfigurationsContext
{
    [Fact]
    public async Task happy_path_test_with_inline_projection()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

            scenario.DocumentShouldExist<User>(id1);
            scenario.DocumentShouldExist<User>(id2);
            scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));

            scenario.Append(Guid.NewGuid(), new DeleteUser {UserId = id2});

            scenario.DocumentShouldExist<User>(id1);
            scenario.DocumentShouldNotExist<User>(id2);
            scenario.DocumentShouldExist<User>(id3);

        });
    }
    [Fact]
    public async Task happy_path_test_with_inline_projection_multi_tenanted()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Schema.For<User>().MultiTenanted();
        });

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            scenario.TenantId = "Purple";

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

            scenario.DocumentShouldExist<User>(id1);
            scenario.DocumentShouldExist<User>(id2);
            scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));

            scenario.Append(Guid.NewGuid(), new DeleteUser {UserId = id2});

            scenario.DocumentShouldExist<User>(id1);
            scenario.DocumentShouldNotExist<User>(id2);
            scenario.DocumentShouldExist<User>(id3);

        });
    }

    [Fact]
    public async Task happy_path_test_with_live_projection_multi_tenanted()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<LiveUser>();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        var id = Guid.NewGuid();

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            scenario.Append(id, new CreateUser { UserId = id, UserName = "Kareem" });
            scenario.DocumentShouldNotExist<User>(id);
        });

        var user = await theSession.Events.AggregateStreamAsync<LiveUser>(id);
        user.ShouldNotBeNull();
        user.UserName.ShouldBe("Kareem");
    }

    [Fact]
    public async Task sad_path_test_with_inline_projection()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        await Should.ThrowAsync<ProjectionScenarioException>(async () =>
        {
            await theStore.Advanced.EventProjectionScenario(scenario =>
            {
                var id1 = Guid.NewGuid();
                var id2 = Guid.NewGuid();
                var id3 = Guid.NewGuid();

                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

                scenario.DocumentShouldExist<User>(id1);
                scenario.DocumentShouldExist<User>(id2);
                scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));

                scenario.Append(Guid.NewGuid(), new DeleteUser {UserId = id2});

                // This should have been deleted
                scenario.DocumentShouldExist<User>(id2);

            });
        });


    }

    [Fact]
    public async Task sad_path_test_with_inline_projection_with_document_assertion()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        await Should.ThrowAsync<ProjectionScenarioException>(async () =>
        {
            await theStore.Advanced.EventProjectionScenario(scenario =>
            {
                var id1 = Guid.NewGuid();
                var id2 = Guid.NewGuid();
                var id3 = Guid.NewGuid();

                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

                scenario.DocumentShouldExist<User>(id1, u => u.FirstName.ShouldBe("WRONG"));
                scenario.DocumentShouldExist<User>(id2);
                scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));


            });
        });


    }


    [Fact]
    public async Task happy_path_test_with_inline_projection_async()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
            scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

            scenario.DocumentShouldExist<User>(id1);
            scenario.DocumentShouldExist<User>(id2);
            scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));

            scenario.Append(Guid.NewGuid(), new DeleteUser {UserId = id2});

            scenario.DocumentShouldExist<User>(id1);
            scenario.DocumentShouldNotExist<User>(id2);
            scenario.DocumentShouldExist<User>(id3);

        });
    }

    [Fact]
    public async Task sad_path_test_with_inline_projection_async()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Async);
        });

        await Should.ThrowAsync<ProjectionScenarioException>(async () =>
        {
            await theStore.Advanced.EventProjectionScenario(scenario =>
            {
                var id1 = Guid.NewGuid();
                var id2 = Guid.NewGuid();
                var id3 = Guid.NewGuid();

                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

                scenario.DocumentShouldExist<User>(id1);
                scenario.DocumentShouldExist<User>(id2);
                scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));

                scenario.Append(Guid.NewGuid(), new DeleteUser {UserId = id2});

                // This should have been deleted
                scenario.DocumentShouldExist<User>(id2);

            });
        });


    }

    [Fact]
    public async Task sad_path_test_with_inline_projection_with_document_assertion_async()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Async);
        });

        await Should.ThrowAsync<ProjectionScenarioException>(async () =>
        {
            await theStore.Advanced.EventProjectionScenario(scenario =>
            {
                var id1 = Guid.NewGuid();
                var id2 = Guid.NewGuid();
                var id3 = Guid.NewGuid();

                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id1, UserName = "Kareem"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id2, UserName = "Magic"});
                scenario.Append(Guid.NewGuid(), new CreateUser {UserId = id3, UserName = "James"});

                scenario.DocumentShouldExist<User>(id1, u => u.FirstName.ShouldBe("WRONG"));
                scenario.DocumentShouldExist<User>(id2);
                scenario.DocumentShouldExist<User>(id3, user => user.UserName.ShouldBe("James"));


            });
        });


    }




}

public class CreateUser
{
    public Guid UserId { get; set; }
    public string UserName { get; set; }
}

public class DeleteUser
{
    public Guid UserId { get; set; }
}

#region sample_user_projection_of_event_projection

public class UserProjection: EventProjection
{
    public User Create(CreateUser create)
    {
        return new User
        {
            Id = create.UserId, UserName = create.UserName
        };
    }

    public void Project(DeleteUser deletion, IDocumentOperations operations)
    {
        operations.Delete<User>(deletion.UserId);
    }
}

#endregion

public class LiveUser
{
    public Guid Id { get; set; }
    public string UserName { get; set; }

    public static LiveUser Create(CreateUser create) =>
        new()
        {
            Id = create.UserId,
            UserName = create.UserName
        };
}

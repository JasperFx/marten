using System;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Events.TestSupport;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("projection_scenario")]
    public class event_projection_scenario_tests : OneOffConfigurationsContext
    {
        public event_projection_scenario_tests() : base("projection_scenario")
        {
        }

        [Fact]
        public async Task happy_path_test_with_inline_projection()
        {
            StoreOptions(opts =>
            {
                opts.Events.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
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
        public async Task sad_path_test_with_inline_projection()
        {
            StoreOptions(opts =>
            {
                opts.Events.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
            });

            await Exception<ProjectionScenarioException>.ShouldBeThrownByAsync(async () =>
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
                opts.Events.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
            });

            await Exception<ProjectionScenarioException>.ShouldBeThrownByAsync(async () =>
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
                opts.Events.Projections.Add(new UserProjection(), ProjectionLifecycle.Async);
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
        public async Task sad_path_test_with_inline_projection_async()
        {
            StoreOptions(opts =>
            {
                opts.Events.Projections.Add(new UserProjection(), ProjectionLifecycle.Async);
            });

            await Exception<ProjectionScenarioException>.ShouldBeThrownByAsync(async () =>
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
                opts.Events.Projections.Add(new UserProjection(), ProjectionLifecycle.Async);
            });

            await Exception<ProjectionScenarioException>.ShouldBeThrownByAsync(async () =>
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
}

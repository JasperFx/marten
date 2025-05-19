using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.ForeignKeys;

public class foreign_keys: OneOffConfigurationsContext
{
    [Fact]
    public void can_insert_document_with_null_value_of_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var issue = new Issue();

        ShouldProperlySave(issue);
    }

    [Fact]
    public async Task can_insert_document_with_existing_value_of_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            await session.SaveChangesAsync();
        }

        var issue = new Issue { AssigneeId = user.Id };

        ShouldProperlySave(issue);
    }

    [Fact]
    public void cannot_insert_document_with_non_existing_value_of_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var issue = new Issue { AssigneeId = Guid.NewGuid() };

        Should.Throw<Marten.Exceptions.MartenCommandException>(async () =>
        {
            using var session = theStore.LightweightSession();
            session.Insert(issue);
            await session.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task can_update_document_with_existing_value_of_foreign_key_to_other_existing_value()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var otherUser = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user, otherUser);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        issue.AssigneeId = otherUser.Id;

        ShouldProperlySave(issue);
    }

    [Fact]
    public async Task can_update_document_with_existing_value_of_foreign_key_to_null()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var otherUser = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user, otherUser);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        issue.AssigneeId = null;

        ShouldProperlySave(issue);
    }

    [Fact]
    public async Task cannot_update_document_with_existing_value_of_foreign_key_to_not_existing()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var otherUser = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user, otherUser);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        issue.AssigneeId = Guid.NewGuid();

        await Should.ThrowAsync<Marten.Exceptions.MartenCommandException>(async () =>
        {
            using (var session = theStore.LightweightSession())
            {
                session.Update(issue);
                await session.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task can_delete_document_with_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Cascade);

        var user = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete(issue);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Issue>(issue.Id)).ShouldBeNull();
            (await query.LoadAsync<User>(user.Id)).ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task can_delete_document_that_is_referenced_by_foreignkey_with_cascadedeletes_from_other_document()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Cascade);

        var user = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete(user);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Issue>(issue.Id)).ShouldBeNull();
            (await query.LoadAsync<User>(user.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task cannot_delete_document_that_is_referenced_by_foreignkey_without_cascadedeletes_from_other_document()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        Should.Throw<Marten.Exceptions.MartenCommandException>(async () =>
        {
            using (var session = theStore.LightweightSession())
            {
                session.Delete(user);
                await session.SaveChangesAsync();
            }
        });

        using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Issue>(issue.Id)).ShouldNotBeNull();
            (await query.LoadAsync<User>(user.Id)).ShouldNotBeNull();
        }
    }

    private void ConfigureForeignKeyWithCascadingDeletes(CascadeAction onDelete)
    {
        StoreOptions(options =>
        {
            options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.OnDelete = onDelete);
        });
    }

    private async Task ShouldProperlySave(Issue issue)
    {
        using (var session = theStore.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Issue>(issue.Id)).ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task persist_and_overwrite_foreign_key()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
        });

        var issue = new Issue();
        var user = new User();

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        issue.AssigneeId = user.Id;

        using (var session = theStore.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        issue.AssigneeId = null;

        using (var session = theStore.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task throws_exception_if_trying_to_delete_referenced_user()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId);
        });

        var issue = new Issue();
        var user = new User();

        issue.AssigneeId = user.Id;

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await Should.ThrowAsync<MartenCommandException>(async () =>
        {
            using (var session = theStore.LightweightSession())
            {
                session.Delete(user);
                await session.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task persist_without_referenced_user()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Issue());
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task order_inserts()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId);
        });

        var issue = new Issue();
        var user = new User();

        issue.AssigneeId = user.Id;

        using var session = theStore.LightweightSession();
        session.Store(issue);
        session.Store(user);

        await session.SaveChangesAsync();
    }

    [Fact]
    public void throws_exception_on_cyclic_dependency()
    {
        Should.Throw<InvalidOperationException>(() =>
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Node1>().ForeignKey<Node3>(x => x.Link);
                _.Schema.For<Node2>().ForeignKey<Node1>(x => x.Link);
                _.Schema.For<Node3>().ForeignKey<Node2>(x => x.Link);
            });
        }).Message.ShouldContain("Cyclic", Case.Insensitive);

    }

    [Fact]
    public async Task id_can_be_a_foreign_key()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Node2>().ForeignKey<Node1>(x => x.Id, fkc => fkc.OnDelete = CascadeAction.Cascade);
        });

        var node1 = new Node1 { Id = Guid.NewGuid() };
        var node2 = new Node2 { Id = node1.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(node1);
            session.Store(node2);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            node1 = await session.LoadAsync<Node1>(node1.Id);
            node2 = await session.LoadAsync<Node2>(node2.Id);
            node1.ShouldNotBeNull();
            node2.ShouldNotBeNull();
            session.Delete(node1);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<Node2>(node2.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task non_standard_id_can_be_a_foreign_key()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Node2>()
                .Identity(x => x.NonStandardId)
                .ForeignKey<Node1>(x => x.NonStandardId, fkc => fkc.OnDelete = CascadeAction.Cascade);
        });

        var node1 = new Node1 { Id = Guid.NewGuid() };
        var node2 = new Node2 { NonStandardId = node1.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(node1);
            session.Store(node2);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            node1 = await session.LoadAsync<Node1>(node1.Id);
            node2 = await session.LoadAsync<Node2>(node2.NonStandardId);
            node1.ShouldNotBeNull();
            node2.ShouldNotBeNull();
            session.Delete(node1);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<Node2>(node2.Id)).ShouldBeNull();
        }
    }

    public class Node1
    {
        public Guid Id { get; set; }
        public Guid Link { get; set; }
    }

    public class Node2
    {
        public Guid Id { get; set; }
        public Guid Link { get; set; }
        public Guid NonStandardId { get; set; }
    }

    public class Node3
    {
        public Guid Id { get; set; }
        public Guid Link { get; set; }
    }

}

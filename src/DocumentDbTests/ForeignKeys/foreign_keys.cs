using System;
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
    public void can_insert_document_with_existing_value_of_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.SaveChanges();
        }

        var issue = new Issue { AssigneeId = user.Id };

        ShouldProperlySave(issue);
    }

    [Fact]
    public void cannot_insert_document_with_non_existing_value_of_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var issue = new Issue { AssigneeId = Guid.NewGuid() };

        Should.Throw<Marten.Exceptions.MartenCommandException>(() =>
        {
            using var session = theStore.LightweightSession();
            session.Insert(issue);
            session.SaveChanges();
        });
    }

    [Fact]
    public void can_update_document_with_existing_value_of_foreign_key_to_other_existing_value()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var otherUser = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user, otherUser);
            session.Store(issue);
            session.SaveChanges();
        }

        issue.AssigneeId = otherUser.Id;

        ShouldProperlySave(issue);
    }

    [Fact]
    public void can_update_document_with_existing_value_of_foreign_key_to_null()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var otherUser = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user, otherUser);
            session.Store(issue);
            session.SaveChanges();
        }

        issue.AssigneeId = null;

        ShouldProperlySave(issue);
    }

    [Fact]
    public void cannot_update_document_with_existing_value_of_foreign_key_to_not_existing()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var otherUser = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user, otherUser);
            session.Store(issue);
            session.SaveChanges();
        }

        issue.AssigneeId = Guid.NewGuid();

        Should.Throw<Marten.Exceptions.MartenCommandException>(() =>
        {
            using (var session = theStore.LightweightSession())
            {
                session.Update(issue);
                session.SaveChanges();
            }
        });
    }

    [Fact]
    public void can_delete_document_with_foreign_key()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Cascade);

        var user = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete(issue);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            SpecificationExtensions.ShouldBeNull(query.Load<Issue>(issue.Id));
            SpecificationExtensions.ShouldNotBeNull(query.Load<User>(user.Id));
        }
    }

    [Fact]
    public void can_delete_document_that_is_referenced_by_foreignkey_with_cascadedeletes_from_other_document()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Cascade);

        var user = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete(user);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            SpecificationExtensions.ShouldBeNull(query.Load<Issue>(issue.Id));
            SpecificationExtensions.ShouldBeNull(query.Load<User>(user.Id));
        }
    }

    [Fact]
    public void cannot_delete_document_that_is_referenced_by_foreignkey_without_cascadedeletes_from_other_document()
    {
        ConfigureForeignKeyWithCascadingDeletes(CascadeAction.Restrict);

        var user = new User();
        var issue = new Issue { AssigneeId = user.Id };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            session.SaveChanges();
        }

        Should.Throw<Marten.Exceptions.MartenCommandException>(() =>
        {
            using (var session = theStore.LightweightSession())
            {
                session.Delete(user);
                session.SaveChanges();
            }
        });

        using (var query = theStore.QuerySession())
        {
            SpecificationExtensions.ShouldNotBeNull(query.Load<Issue>(issue.Id));
            SpecificationExtensions.ShouldNotBeNull(query.Load<User>(user.Id));
        }
    }

    private void ConfigureForeignKeyWithCascadingDeletes(CascadeAction onDelete)
    {
        StoreOptions(options =>
        {
            options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.OnDelete = onDelete);
        });
    }

    private void ShouldProperlySave(Issue issue)
    {
        using (var session = theStore.LightweightSession())
        {
            session.Store(issue);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            var documentFromDb = query.Load<Issue>(issue.Id);

            SpecificationExtensions.ShouldNotBeNull(documentFromDb);
        }
    }

    [Fact]
    public void persist_and_overwrite_foreign_key()
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
            session.SaveChanges();
        }

        issue.AssigneeId = user.Id;

        using (var session = theStore.LightweightSession())
        {
            session.Store(issue);
            session.SaveChanges();
        }

        issue.AssigneeId = null;

        using (var session = theStore.LightweightSession())
        {
            session.Store(issue);
            session.SaveChanges();
        }
    }

    [Fact]
    public void throws_exception_if_trying_to_delete_referenced_user()
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
            session.SaveChanges();
        }

        Exception<Marten.Exceptions.MartenCommandException>.ShouldBeThrownBy(() =>
        {
            using (var session = theStore.LightweightSession())
            {
                session.Delete(user);
                session.SaveChanges();
            }
        });
    }

    [Fact]
    public void persist_without_referenced_user()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Issue());
            session.SaveChanges();
        }
    }

    [Fact]
    public void order_inserts()
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

        session.SaveChanges();
    }

    [Fact]
    public void throws_exception_on_cyclic_dependency()
    {
        Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
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
    public void id_can_be_a_foreign_key()
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
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            node1 = session.Load<Node1>(node1.Id);
            node2 = session.Load<Node2>(node2.Id);
            node1.ShouldNotBeNull();
            node2.ShouldNotBeNull();
            session.Delete(node1);
            session.SaveChanges();
        }

        using (var session = theStore.QuerySession())
        {
            session.Load<Node2>(node2.Id).ShouldBeNull();
        }
    }

    [Fact]
    public void non_standard_id_can_be_a_foreign_key()
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
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            node1 = session.Load<Node1>(node1.Id);
            node2 = session.Load<Node2>(node2.NonStandardId);
            node1.ShouldNotBeNull();
            node2.ShouldNotBeNull();
            session.Delete(node1);
            session.SaveChanges();
        }

        using (var session = theStore.QuerySession())
        {
            session.Load<Node2>(node2.Id).ShouldBeNull();
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

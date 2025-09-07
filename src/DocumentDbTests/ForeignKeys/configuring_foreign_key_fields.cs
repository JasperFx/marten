using System;
using System.Linq;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.ForeignKeys;

public class configuring_foreign_key_fields : OneOffConfigurationsContext
{

    [Fact]
    public void should_get_foreign_key_from_attribute()
    {
        theStore.StorageFeatures.MappingFor(typeof(Issue))
            .As<DocumentMapping>()
            .ForeignKeys
            .ShouldContain(x => x.ColumnNames[0] == "user_id");
    }

    [Fact]
    public void should_get_foreign_key_from_registry()
    {
        var storeOptions = new StoreOptions();
        storeOptions.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);

        var store = StoreOptions(_ =>
        {
            _.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);
        });

        store.StorageFeatures.MappingFor(typeof(Issue))
            .As<DocumentMapping>()
            .ForeignKeys
            .ShouldContain(x => x.ColumnNames[0] == "other_user_id");
    }

    [Fact]
    public void should_allow_self_reference()
    {
        theStore.StorageFeatures.MappingFor(typeof(Employee))
            .As<DocumentMapping>()
            .ForeignKeys
            .ShouldContain(x => x.ColumnNames[0] == "manager_id");
    }

    [Fact]
    public void should_allow_foreign_key_on_id_field()
    {

        var store = StoreOptions(_ =>
        {
            _.Schema.For<Foo>()
                .Identity(x => x.FooId);
            _.Schema.For<FooExtra>()
                .Identity(x => x.FooId)
                .ForeignKey<Foo>(x => x.FooId);
        });

        store.StorageFeatures.MappingFor(typeof(FooExtra))
            .As<DocumentMapping>()
            .ForeignKeys
            .ShouldContain(x => x.ColumnNames[0] == "id");
    }

    [Fact]
    public void should_not_include_tenant_id_in_foreign_key_from_tenanted_doc_to_not_tenanted_doc()
    {
        var store = StoreOptions(_ =>
        {
            _.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, c =>
            {
                c.WithTenants("tenant1").InDatabaseNamed("postgres");
            });

            _.Schema.For<Foo>()
                .SingleTenanted()
                .Identity(x => x.FooId);
            _.Schema.For<Bar>()
                .MultiTenanted()
                .Identity(x => x.BarId)
                .ForeignKey<Foo>(x => x.FooId);
        });

        var mapping = store.Options.Storage.MappingFor(typeof(Bar));
        new DocumentTable(mapping).ForeignKeys.Single().ColumnNames.ShouldNotContain(TenantIdColumn.Name);
    }

    [Fact]
    public void should_include_tenant_id_in_foreign_key_between_tenanted_docs()
    {
        var store = StoreOptions(_ =>
        {
            _.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, c =>
            {
                c.WithTenants("tenant1").InDatabaseNamed("postgres");
            });

            _.Schema.For<Foo>()
                .MultiTenanted()
                .Identity(x => x.FooId);
            _.Schema.For<Bar>()
                .MultiTenanted()
                .Identity(x => x.BarId)
                .ForeignKey<Foo>(x => x.FooId);
        });

        var mapping = store.Options.Storage.MappingFor(typeof(Bar));
        new DocumentTable(mapping).ForeignKeys.Single().ColumnNames.ShouldContain(TenantIdColumn.Name);
    }

    [Fact]
    public void should_not_include_tenant_id_between_single_tenanted_docs()
    {
        var store = StoreOptions(_ =>
        {
            _.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, c =>
            {
                c.WithTenants("tenant1").InDatabaseNamed("postgres");
            });

            _.Schema.For<Foo>()
                .SingleTenanted()
                .Identity(x => x.FooId);
            _.Schema.For<Bar>()
                .SingleTenanted()
                .Identity(x => x.BarId)
                .ForeignKey<Foo>(x => x.FooId);
        });

        var mapping = store.Options.Storage.MappingFor(typeof(Bar));
        new DocumentTable(mapping).ForeignKeys.Single().ColumnNames.ShouldNotContain(TenantIdColumn.Name);
    }

    #region sample_issue-with-fk-attribute
    public class Issue
    {
        public Issue()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        [ForeignKey(typeof(User))]
        public Guid UserId { get; set; }

        public Guid? OtherUserId { get; set; }
    }

    #endregion

    public class User
    {
        public User()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string UserName { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public bool Internal { get; set; }

        public string FullName => "${FirstName} {LastName}";

        public int Age { get; set; }

        public string ToJson()
        {
            return $"{{\"Id\": \"{Id}\", \"Age\": {Age}, \"FullName\": \"{FullName}\", \"Internal\": {Internal.ToString().ToLowerInvariant()}, \"LastName\": \"{LastName}\", \"UserName\": \"{UserName}\", \"FirstName\": \"{FirstName}\"}}";
        }

        public void From(User user)
        {
            Id = user.Id;
        }
    }



    public class Employee
    {
        public Employee()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        [ForeignKey(typeof(Employee))]
        public Guid? ManagerId { get; set; }
    }

    public class Foo
    {
        public Guid FooId { get; set; }
    }
    public class FooExtra
    {
        public Guid FooId { get; set; }
    }

    public class Bar
    {
        public Guid BarId { get; set; }
        public Guid FooId { get; set; }
    }
}

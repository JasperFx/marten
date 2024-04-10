using System;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests;

public class DocumentSchemaResolverTests : OneOffConfigurationsContext
{
    [Fact]
    public void ValidateDefaultSchemaNames()
    {
        StoreOptions(options =>
        {
            var newOptions = new StoreOptions();
            options.DatabaseSchemaName = newOptions.DatabaseSchemaName;
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("public", schema.DatabaseSchemaName);
        Assert.Equal("public", schema.EventsSchemaName);
    }

    [Fact]
    public void ValidateCustomSchemaNames()
    {
        StoreOptions(options =>
        {
            options.DatabaseSchemaName = "custom_schema_name";
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("custom_schema_name", schema.DatabaseSchemaName);
        Assert.Equal("custom_schema_name", schema.EventsSchemaName);
    }

    [Fact]
    public void ValidateCustomEventSchemaNames()
    {
        StoreOptions(options =>
        {
            options.DatabaseSchemaName = "custom_schema_name";
            options.Events.DatabaseSchemaName = "custom_event_schema_name";
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("custom_schema_name", schema.DatabaseSchemaName);
        Assert.Equal("custom_event_schema_name", schema.EventsSchemaName);
    }

    [Fact]
    public void ValidateRegisteredDocumentNames()
    {
        StoreOptions(options =>
        {
            options.RegisterDocumentType<Account>();
            options.RegisterDocumentType<Company>();
            options.RegisterDocumentType<User>();
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("documentschemaresolvertests.mt_doc_account", schema.For<Account>());
        Assert.Equal("documentschemaresolvertests.mt_doc_company", schema.For<Company>());
        Assert.Equal("documentschemaresolvertests.mt_doc_user", schema.For<User>());

        Assert.Equal("mt_doc_account", schema.For<Account>(qualified: false));
        Assert.Equal("mt_doc_company", schema.For<Company>(qualified: false));
        Assert.Equal("mt_doc_user", schema.For<User>(qualified: false));
    }

    [Fact]
    public void ValidateUnregisteredDocumentNames()
    {
        StoreOptions(_ => { }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("documentschemaresolvertests.mt_doc_account", schema.For<Account>());
        Assert.Equal("documentschemaresolvertests.mt_doc_company", schema.For<Company>());
        Assert.Equal("documentschemaresolvertests.mt_doc_user", schema.For<User>());

        Assert.Equal("mt_doc_account", schema.For<Account>(qualified: false));
        Assert.Equal("mt_doc_company", schema.For<Company>(qualified: false));
        Assert.Equal("mt_doc_user", schema.For<User>(qualified: false));
    }

    [Fact]
    public void ValidateDocumentWithCustomTableNames()
    {
        StoreOptions(options =>
        {
            options.RegisterDocumentType<Account>();
            options.Schema.For<Account>().DatabaseSchemaName("custom_doc_schema");
            options.Schema.For<Account>().DocumentAlias("custom_account");
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("custom_doc_schema.mt_doc_custom_account", schema.For<Account>());

        Assert.Equal("mt_doc_custom_account", schema.For<Account>(qualified: false));
    }

    [Fact]
    public void ValidateEventTableNames()
    {
        StoreOptions(_ => { }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("documentschemaresolvertests.mt_streams", schema.ForStreams());
        Assert.Equal("documentschemaresolvertests.mt_events", schema.ForEvents());
        Assert.Equal("documentschemaresolvertests.mt_event_progression", schema.ForEventProgression());

        Assert.Equal("mt_streams", schema.ForStreams(qualified: false));
        Assert.Equal("mt_events", schema.ForEvents(qualified: false));
        Assert.Equal("mt_event_progression", schema.ForEventProgression(qualified: false));
    }

    [Fact]
    public void ValidateEventTableNamesWithCustomSchema()
    {
        StoreOptions(options =>
        {
            options.Events.DatabaseSchemaName = "custom_event_schema_name";
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal("custom_event_schema_name.mt_streams", schema.ForStreams());
        Assert.Equal("custom_event_schema_name.mt_events", schema.ForEvents());
        Assert.Equal("custom_event_schema_name.mt_event_progression", schema.ForEventProgression());

        Assert.Equal("mt_streams", schema.ForStreams(qualified: false));
        Assert.Equal("mt_events", schema.ForEvents(qualified: false));
        Assert.Equal("mt_event_progression", schema.ForEventProgression(qualified: false));
    }

    [Fact]
    public void ValidateProjectionNames()
    {
        StoreOptions(options =>
        {
            options.Projections.Snapshot<ProjectionA>(SnapshotLifecycle.Inline);
            options.Projections.Snapshot<ProjectionB>(SnapshotLifecycle.Async);
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        Assert.Equal(
            "documentschemaresolvertests.mt_doc_documentschemaresolvertests_projectiona",
            schema.For<ProjectionA>());
        Assert.Equal("documentschemaresolvertests.mt_doc_custom_projection_alias", schema.For<ProjectionB>());

        Assert.Equal("mt_doc_documentschemaresolvertests_projectiona", schema.For<ProjectionA>(qualified: false));
        Assert.Equal("mt_doc_custom_projection_alias",                 schema.For<ProjectionB>(qualified: false));
    }

    public record FooEvent;

    public record ProjectionA(Guid Id)
    {
        public static ProjectionA Create(IEvent<FooEvent> @event)
        {
            return new ProjectionA(@event.StreamId);
        }
    }

    [DocumentAlias("custom_projection_alias")]
    public record ProjectionB(Guid Id)
    {
        public static ProjectionB Create(IEvent<FooEvent> @event)
        {
            return new ProjectionB(@event.StreamId);
        }
    }
}

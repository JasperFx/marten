using System;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
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

        #region sample_document_schema_resolver_resolve_schemas
        var schema = theSession.DocumentStore.Options.Schema;

        schema.DatabaseSchemaName.ShouldBe("public");
        schema.EventsSchemaName.ShouldBe("public");
        #endregion
    }

    [Fact]
    public void ValidateCustomSchemaNames()
    {
        StoreOptions(options =>
        {
            options.DatabaseSchemaName = "custom_schema_name";
        }, false);

        #region sample_document_schema_resolver_options
        var schema = theSession.DocumentStore.Options.Schema;
        #endregion

        schema.DatabaseSchemaName.ShouldBe("custom_schema_name");
        schema.EventsSchemaName.ShouldBe("custom_schema_name");
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

        schema.DatabaseSchemaName.ShouldBe("custom_schema_name");
        schema.EventsSchemaName.ShouldBe("custom_event_schema_name");
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

        schema.For(typeof(Account)).ShouldBe("documentschemaresolvertests.mt_doc_account");
        schema.For<Company>().ShouldBe("documentschemaresolvertests.mt_doc_company");
        schema.For<User>().ShouldBe("documentschemaresolvertests.mt_doc_user");

        schema.For<Account>(qualified: false).ShouldBe("mt_doc_account");
        schema.For<Company>(qualified: false).ShouldBe("mt_doc_company");
        schema.For<User>(qualified: false).ShouldBe("mt_doc_user");
    }

    [Fact]
    public void ValidateUnregisteredDocumentNames()
    {
        StoreOptions(options =>
        {
            var newOptions = new StoreOptions();
            options.DatabaseSchemaName = newOptions.DatabaseSchemaName;
        }, false);

        #region sample_document_schema_resolver_resolve_documents
        var schema = theSession.DocumentStore.Options.Schema;

        schema.For<Account>().ShouldBe("public.mt_doc_account");
        schema.For<Company>().ShouldBe("public.mt_doc_company");
        schema.For<User>().ShouldBe("public.mt_doc_user");

        // `qualified: false` returns the table name without schema
        schema.For<Account>(qualified: false).ShouldBe("mt_doc_account");
        schema.For<Company>(qualified: false).ShouldBe("mt_doc_company");
        schema.For<User>(qualified: false).ShouldBe("mt_doc_user");
        #endregion
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

        schema.For<Account>().ShouldBe("custom_doc_schema.mt_doc_custom_account");

        schema.For<Account>(qualified: false).ShouldBe("mt_doc_custom_account");
    }

    [Fact]
    public void ValidateEventTableNames()
    {
        StoreOptions(options => {
            var newOptions = new StoreOptions();
            options.DatabaseSchemaName = newOptions.DatabaseSchemaName;
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        #region sample_document_schema_resolver_resolve_event_tables

        schema.ForStreams().ShouldBe("public.mt_streams");
        schema.ForEvents().ShouldBe("public.mt_events");
        schema.ForEventProgression().ShouldBe("public.mt_event_progression");

        schema.ForStreams(qualified: false).ShouldBe("mt_streams");
        schema.ForEvents(qualified: false).ShouldBe("mt_events");
        schema.ForEventProgression(qualified: false).ShouldBe("mt_event_progression");
        #endregion
    }

    [Fact]
    public void ValidateEventTableNamesWithCustomSchema()
    {
        StoreOptions(options =>
        {
            options.Events.DatabaseSchemaName = "custom_event_schema_name";
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        schema.ForStreams().ShouldBe("custom_event_schema_name.mt_streams");
        schema.ForEvents().ShouldBe("custom_event_schema_name.mt_events");
        schema.ForEventProgression().ShouldBe("custom_event_schema_name.mt_event_progression");

        schema.ForStreams(qualified: false).ShouldBe("mt_streams");
        schema.ForEvents(qualified: false).ShouldBe("mt_events");
        schema.ForEventProgression(qualified: false).ShouldBe("mt_event_progression");
    }

    [Fact]
    public void ValidateProjectionNames()
    {
        StoreOptions(options =>
        {
            options.Projections.Snapshot<ProjectionA>(ProjectionLifecycle.Inline);
            options.Projections.Snapshot<ProjectionB>(ProjectionLifecycle.Async);
        }, false);

        var schema = theSession.DocumentStore.Options.Schema;

        schema.For<ProjectionA>().ShouldBe("documentschemaresolvertests.mt_doc_documentschemaresolvertests_projectiona");
        schema.For<ProjectionB>().ShouldBe("documentschemaresolvertests.mt_doc_custom_projection_alias");

        schema.For<ProjectionA>(qualified: false).ShouldBe("mt_doc_documentschemaresolvertests_projectiona");
        schema.For<ProjectionB>(qualified: false).ShouldBe("mt_doc_custom_projection_alias");
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

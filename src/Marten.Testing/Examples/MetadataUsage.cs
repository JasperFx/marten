using System;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class MetadataUsage
{
    public void DisableAllInformationalFields()
    {
        #region sample_DisableAllInformationalFields

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // This will direct Marten to omit all informational
            // metadata fields
            opts.Policies.DisableInformationalFields();
        });

        #endregion
    }

    public void EnablingCausation()
    {
        #region sample_enabling_causation_fields

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Optionally turn on metadata columns by document type
            opts.Schema.For<User>().Metadata(x =>
            {
                x.CorrelationId.Enabled = true;
                x.CausationId.Enabled = true;
                x.Headers.Enabled = true;

            });

            // Or just globally turn on columns for all document
            // types in one fell swoop
            opts.Policies.ForAllDocuments(x =>
            {
                x.Metadata.CausationId.Enabled = true;
                x.Metadata.CorrelationId.Enabled = true;
                x.Metadata.Headers.Enabled = true;

                // This column is "opt in"
                x.Metadata.CreatedAt.Enabled = true;
            });
        });

        #endregion


    }

    #region sample_setting_metadata_on_session

    public void SettingMetadata(IDocumentSession session, string correlationId, string causationId)
    {
        // These values will be persisted to any document changed
        // by the session when SaveChanges() is called
        session.CorrelationId = correlationId;
        session.CausationId = causationId;
    }

    #endregion

    #region sample_set_header

    public void SetHeader(IDocumentSession session, string sagaId)
    {
        session.SetHeader("saga-id", sagaId);
    }

    #endregion

    #region sample_DocWithMetadata

    public class DocWithMetadata
    {
        public Guid Id { get; set; }

        // other members

        public Guid Version { get; set; }
        public string Causation { get; set; }
        public bool IsDeleted { get; set; }
    }

    #endregion

    public void explicitly_map_metadata()
    {
        #region sample_explicitly_map_metadata

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Explicitly map the members on this document type
            // to metadata columns.
            opts.Schema.For<DocWithMetadata>().Metadata(m =>
            {
                m.Version.MapTo(x => x.Version);
                m.CausationId.MapTo(x => x.Causation);
                m.IsSoftDeleted.MapTo(x => x.IsDeleted);
            });
        });

        #endregion
    }

    public void ConfigureEventMetadata()
    {
        #region sample_ConfigureEventMetadata

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("connection string");

            // This adds additional metadata tracking to the
            // event store tables
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
        });

        #endregion
    }
}

using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using System;
using Xunit;

namespace DocumentDbTests.Configuration;

public class document_policies_for_metadata: OneOffConfigurationsContext
{
    public document_policies_for_metadata()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<UserMetadata>();
        });
    }

    [Fact]
    public void document_policy_for_metadata_defaults()
    {
        StoreOptions(_ => { });

        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.Version.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.LastModified.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CreatedAt.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.TenantId.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.IsSoftDeleted.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.SoftDeletedAt.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.DocumentType.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.DotNetType.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CausationId.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CorrelationId.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.LastModifiedBy.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.Headers.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void document_policy_for_metadata_all_enabled()
    {
        StoreOptions(_ =>
        {
            _.Policies.ForAllDocuments(x => x.Metadata.Version.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.LastModified.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.CreatedAt.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.TenantId.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.IsSoftDeleted.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.SoftDeletedAt.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.DocumentType.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.DocumentType.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.CausationId.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.CorrelationId.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.LastModifiedBy.Enabled = true);
            _.Policies.ForAllDocuments(x => x.Metadata.Headers.Enabled = true);
        });

        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.Version.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.LastModified.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CreatedAt.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.TenantId.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.IsSoftDeleted.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.SoftDeletedAt.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.DocumentType.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.DocumentType.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CausationId.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CorrelationId.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.LastModifiedBy.Enabled.ShouldBeTrue();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.Headers.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void document_policy_for_metadata_all_disabled()
    {
        StoreOptions(_ =>
        {
            _.Policies.ForAllDocuments(x => x.Metadata.Version.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.LastModified.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.CreatedAt.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.TenantId.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.IsSoftDeleted.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.SoftDeletedAt.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.DocumentType.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.DocumentType.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.CausationId.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.CorrelationId.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.LastModifiedBy.Enabled = false);
            _.Policies.ForAllDocuments(x => x.Metadata.Headers.Enabled = false);
        });

        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.Version.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.LastModified.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CreatedAt.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.TenantId.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.IsSoftDeleted.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.SoftDeletedAt.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.DocumentType.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.DocumentType.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CausationId.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.CorrelationId.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.LastModifiedBy.Enabled.ShouldBeFalse();
        TheStore.StorageFeatures.MappingFor(typeof(UserMetadata)).Metadata.Headers.Enabled.ShouldBeFalse();
    }

    public class UserMetadata
    {
        public Guid Id;
    }
}

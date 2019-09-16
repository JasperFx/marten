using Marten.Storage;
using Marten.Util;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Schema
{
    public class MetadataProjector<T>
    {
        private readonly Dictionary<string, dynamic> _memberSetters = new Dictionary<string, dynamic>();
        private readonly Dictionary<string, dynamic> _memberGetters = new Dictionary<string, dynamic>();

        public bool HasMappings => _memberSetters.Count > 0;

        public MetadataProjector(DocumentMapping mapping)
        {
            CreateMemberGetterSetters(mapping);
        }

        public DocumentMetadata ProjectTo(T entity, DocumentMetadata metadata)
        {
            if (!HasMappings)
            {
                return metadata;
            }

            ProjectMemberValue(DocumentMapping.VersionColumn, entity, metadata.CurrentVersion);
            ProjectMemberValue(DocumentMapping.LastModifiedColumn, entity, metadata.LastModified);
            ProjectMemberValue(TenantIdColumn.Name, entity, metadata.TenantId);
            ProjectMemberValue(DocumentMapping.DocumentTypeColumn, entity, metadata.DocumentType);
            ProjectMemberValue(DocumentMapping.DotNetTypeColumn, entity, metadata.DotNetType);
            ProjectMemberValue(DocumentMapping.DeletedColumn, entity, metadata.Deleted);
            ProjectMemberValue(DocumentMapping.DeletedAtColumn, entity, metadata.DeletedAt);

            return metadata;
        }

        public void Update(T entity, DocumentMetadata metadata)
        {
            if (!HasMappings)
            {
                return;
            }

            ValidateReadOnlyMetadata(entity, metadata);
            ProjectTo(entity, metadata);
        }

        public void UpdateVersion(T entity, Guid newVersion)
        {
            ProjectMemberValue(DocumentMapping.VersionColumn, entity, newVersion);
        }

        private void CreateMemberGetterSetters(DocumentMapping mapping)
        {
            CreateSetter<Guid>(DocumentMapping.VersionColumn, mapping.VersionMember);
            CreateSetter<DateTime>(DocumentMapping.LastModifiedColumn, mapping.LastModifiedMember);

            CreateSetter<string>(TenantIdColumn.Name, mapping.TenantIdMember);
            CreateGetter<string>(TenantIdColumn.Name, mapping.TenantIdMember);

            CreateSetter<string>(DocumentMapping.DocumentTypeColumn, mapping.DocumentTypeMember);
            CreateGetter<string>(DocumentMapping.DocumentTypeColumn, mapping.DocumentTypeMember);

            CreateSetter<string>(DocumentMapping.DotNetTypeColumn, mapping.DotNetTypeMember);
            CreateGetter<string>(DocumentMapping.DotNetTypeColumn, mapping.DotNetTypeMember);

            CreateSetter<bool>(DocumentMapping.DeletedColumn, mapping.IsSoftDeletedMember);
            CreateGetter<bool>(DocumentMapping.DeletedColumn, mapping.IsSoftDeletedMember);

            CreateSetter<DateTime?>(DocumentMapping.DeletedAtColumn, mapping.SoftDeletedAtMember);
            CreateGetter<DateTime?>(DocumentMapping.DeletedAtColumn, mapping.SoftDeletedAtMember);
        }

        private void CreateSetter<TMember>(string memberKey, MemberInfo memberInfo)
        {
            if (memberInfo != null)
            {
                _memberSetters.Add(memberKey, LambdaBuilder.Setter<T, TMember>(memberInfo));
            }
        }

        private void CreateGetter<TMember>(string memberKey, MemberInfo memberInfo)
        {
            if (memberInfo != null)
            {
                _memberGetters.Add(memberKey, LambdaBuilder.Getter<T, TMember>(memberInfo));
            }
        }

        private void ProjectMemberValue<TMember>(string memberKey, T entity, TMember value)
        {
            if (_memberSetters.TryGetValue(memberKey, out var setter))
            {
                (setter as Action<T, TMember>)(entity, value);
            }
        }

        private void ValidateReadOnlyMetadata(T entity, DocumentMetadata metadata)
        {
            ValidateReadOnlyMetadataField(DocumentMapping.DeletedColumn, entity, metadata.Deleted);
            ValidateReadOnlyMetadataField(DocumentMapping.DeletedAtColumn, entity, metadata.DeletedAt);
            ValidateReadOnlyMetadataField(DocumentMapping.DotNetTypeColumn, entity, metadata.DotNetType);
            ValidateReadOnlyMetadataField(DocumentMapping.DocumentTypeColumn, entity, metadata.DocumentType);
            ValidateReadOnlyMetadataField(TenantIdColumn.Name, entity, metadata.TenantId);
        }

        private void ValidateReadOnlyMetadataField<TMember>(string memberKey, T entity, TMember expectedValue)
        {
            if (_memberGetters.TryGetValue(memberKey, out var getter))
            {
                if (!expectedValue.Equals((getter as Func<T, TMember>)(entity)))
                {
                    throw new InvalidOperationException($"The mapped {memberKey} metadata member is read-only.");
                }
            }
        }
    }
}

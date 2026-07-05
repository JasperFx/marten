using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.Operations;

/// <summary>
/// #4596 Phase 2c — tenant-scoped counterpart to <see cref="TruncateTable"/>.
/// Emits <c>DELETE FROM &lt;table&gt; WHERE tenant_id = '&lt;tenantId&gt;'</c> for the
/// document type's mapped table at command-build time (when the session knows the
/// storage / table name). Used by Marten's per-tenant pre-rebuild teardown to wipe
/// only the rebuilding tenant's projected docs without touching the other tenants'
/// rows — TRUNCATE would wipe everyone, which is exactly the cross-tenant
/// corruption jasperfx#407 Phase 2b's per-tenant rebuild path is designed to
/// avoid. The tenant id arrives as a Marten-registered partition suffix
/// (validated against <c>AdvancedOperations.AssertValidPostgresqlIdentifiers</c>:
/// letters, digits, underscores) so it cannot carry SQL-injection shape.
/// </summary>
internal class DeleteAllForTenant: IStorageOperation
{
    private readonly string _tableName;
    private readonly string _tenantId;

    public DeleteAllForTenant(Type documentType, string tenantId)
    {
        DocumentType = documentType;
        _tenantId = tenantId;
    }

    public DeleteAllForTenant(string qualifiedTableName, string tenantId)
    {
        _tableName = qualifiedTableName;
        _tenantId = tenantId;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var name = _tableName ?? session.StorageFor(DocumentType).TableName.QualifiedName;
        builder.Append($"delete from {name} where tenant_id = '");
        builder.Append(_tenantId);
        builder.Append("'");
    }

    public Type DocumentType { get; }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }

    public override string ToString()
    {
        return $"Delete data for tenant '{_tenantId}' from: {DocumentType?.FullName ?? _tableName}";
    }
}

using System;
using Marten.Schema;
using Weasel.Core.Migrations;

namespace Marten.Storage
{
    public interface ITenancy : IDatabaseSource
    {
        Tenant GetTenant(string tenantId);
        Tenant Default { get; }
        IDocumentCleaner Cleaner { get; }

    }
}

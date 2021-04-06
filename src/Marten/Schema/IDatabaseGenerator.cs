using System;
using Marten.Storage;
#nullable enable
namespace Marten.Schema
{
    public interface IDatabaseGenerator
    {
        void CreateDatabases(ITenancy tenancy, Action<IDatabaseCreationExpressions> configure);
    }
}

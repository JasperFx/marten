using System;
using Marten.Storage;

namespace Marten.Schema
{
    public interface IDatabaseGenerator
    {
        void CreateDatabases(ITenancy tenancy, Action<IDatabaseCreationExpressions> configure);
    }
}
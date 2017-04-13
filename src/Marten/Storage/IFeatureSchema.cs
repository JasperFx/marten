using System;
using System.Collections.Generic;

namespace Marten.Storage
{
    public interface IFeatureSchema
    {
        IEnumerable<Type> DependentTypes();
        bool IsActive { get; }
        ISchemaObject[] Objects { get; }
        Type StorageType { get; }

        // TODO -- write permissions. Stupid DDL template stuff
        // for our idiot database team's endless bikeshedding
    }
}
using System;
using System.Collections.Generic;

namespace Marten.Storage
{
    public interface IFeatureSchema
    {
        IEnumerable<Type> DependentTypes();

        bool IsActive { get; }
        string Identifier { get; }
        IEnumerable<ISchemaObject> Objects { get; }

        Type StorageType { get; }
    }
}
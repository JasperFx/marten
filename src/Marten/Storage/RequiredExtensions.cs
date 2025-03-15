using System;
using System.Collections.Generic;
using System.IO;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Storage;

internal class RequiredExtensions: IFeatureSchema
{
    private readonly StoreOptions _options;

    public RequiredExtensions(StoreOptions options)
    {
        _options = options;
    }

    public ISchemaObject[] Objects => [new Extension("unaccent")];

    public string Identifier => "requiredextensions";

    public Migrator Migrator => _options.Advanced.Migrator;

    public Type StorageType => typeof(RequiredExtensions);

    public IEnumerable<Type> DependentTypes()
    {
        yield break;
    }

    public void WritePermissions(Migrator rules, TextWriter writer)
    {
        //Nothing
    }
}

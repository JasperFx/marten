using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Core;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.PLv8.Transforms;

internal class TransformSchema: ITransforms, IFeatureSchema
{
    public const string PatchDoc = "patch_doc";
    private readonly object _lock = new();

    private readonly Ref<ImHashMap<string, TransformFunction>> _functions
        = Ref.Of(ImHashMap<string, TransformFunction>.Empty);

    private readonly StoreOptions _options;

    public TransformSchema(StoreOptions options)
    {
        _options = options;
    }

    public IEnumerable<Type> DependentTypes()
    {
        yield break;
    }

    public ISchemaObject[] Objects => schemaObjects().ToArray();

    public Type StorageType { get; } = typeof(TransformSchema);
    public string Identifier { get; } = "transforms";
    public Migrator Migrator => _options.Advanced.Migrator;

    public void WritePermissions(Migrator rules, TextWriter writer)
    {
        // Nothing
    }

    public void LoadFile(string file, string name = null)
    {
        if (!Path.IsPathRooted(file))
        {
            file = AppContext.BaseDirectory.AppendPath(file);
        }

        var function = TransformFunction.ForFile(_options, file, name);
        AddFunction(function);
    }

    public void LoadDirectory(string directory)
    {
        if (!Path.IsPathRooted(directory))
        {
            directory = AppContext.BaseDirectory.AppendPath(directory);
        }

        FileSystem.FindFiles(directory, FileSet.Deep("*.js")).Each(file =>
        {
            LoadFile(file);
        });
    }

    public void LoadJavascript(string name, string script)
    {
        var func = new TransformFunction(_options, name, script);
        AddFunction(func);
    }

    public void Load(TransformFunction function)
    {
        AddFunction(function);
    }

    public TransformFunction For(string name)
    {
        if (_functions.Value.TryFind(name, out var function))
            return function;

        if (name != PatchDoc)
            throw new ArgumentOutOfRangeException(nameof(name), $"Unknown Transform Name '{name}'");

        return loadPatchDoc();
    }

    public IEnumerable<TransformFunction> AllFunctions()
    {
        return _functions.Value.Enumerate().Select(x => x.Value);
    }

    private void AddFunction(TransformFunction function)
    {
        _functions.Swap(d => d.AddOrKeep(function.Name, function));
    }

    private IEnumerable<ISchemaObject> schemaObjects()
    {
        loadPatchDoc();

        yield return new Extension("PLV8");

        foreach (var function in AllFunctions()) yield return function;
    }

    private TransformFunction loadPatchDoc()
    {
        if (_functions.Value.TryFind(PatchDoc, out var existingFunction))
        {
            return existingFunction;
        }

        lock (_lock)
        {
            if (_functions.Value.TryFind(PatchDoc, out var current))
                return current;

            var stream = GetType().Assembly.GetManifestResourceStream("Marten.PLv8.mt_patching.js");
            var js = stream.ReadAllText().Replace("{databaseSchema}", _options.DatabaseSchemaName);

            var patching = new TransformFunction(_options, PatchDoc, js);
            patching.OtherArgs.Add("patch");

            Load(patching);

            return patching;
        }
    }
}

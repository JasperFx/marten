using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.PLv8.Transforms
{
    internal class TransformSchema: ITransforms, IFeatureSchema
    {
        public const string PatchDoc = "patch_doc";

        private readonly StoreOptions _options;

        private readonly IDictionary<string, TransformFunction> _functions
            = new Dictionary<string, TransformFunction>();

        public TransformSchema(StoreOptions options)
        {
            _options = options;
        }

        private void AddFunction(TransformFunction function)
        {
            if (!_functions.ContainsKey(function.Name))
            {
                _functions.Add(function.Name, function);
            }
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

            new FileSystem().FindFiles(directory, FileSet.Deep("*.js")).Each(file =>
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
            if (!_functions.TryGetValue(name, out var function))
            {
                if (name == PatchDoc)
                {
                    return loadPatchDoc();
                }

                throw new ArgumentOutOfRangeException(nameof(name), $"Unknown Transform Name '{name}'");
            }

            return function;
        }

        public IEnumerable<TransformFunction> AllFunctions()
        {
            return _functions.Values;
        }

        public IEnumerable<Type> DependentTypes()
        {
            yield break;
        }

        public bool IsActive(StoreOptions options)
        {
            return true;
        }

        public ISchemaObject[] Objects => schemaObjects().ToArray();

        private IEnumerable<ISchemaObject> schemaObjects()
        {
            if (!_functions.ContainsKey(PatchDoc))
            {
                loadPatchDoc();
            }

            yield return new Extension("PLV8");

            foreach (var function in _functions.Values)
            {
                yield return function;
            }
        }

        private TransformFunction loadPatchDoc()
        {
            var stream = GetType().Assembly.GetManifestResourceStream("Marten.PLv8.mt_patching.js");
            var js = stream.ReadAllText().Replace("{databaseSchema}", _options.DatabaseSchemaName);

            var patching = new TransformFunction(_options, PatchDoc, js);
            patching.OtherArgs.Add("patch");

            Load(patching);

            return patching;
        }

        public Type StorageType { get; } = typeof(TransformSchema);
        public string Identifier { get; } = "transforms";

        public void WritePermissions(DdlRules rules, TextWriter writer)
        {
            // Nothing
        }
    }
}

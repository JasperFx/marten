using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.Schema;

namespace Marten.Storage
{
    public class SystemFunctions : IFeatureSchema
    {
        private readonly StoreOptions _options;
        private readonly IDictionary<string, SystemFunction> _systemFunctions = new Dictionary<string, SystemFunction>();

        public SystemFunctions(StoreOptions options)
        {
            _options = options;

            AddSystemFunction(options, "mt_immutable_timestamp", "text");
        }

        public void AddSystemFunction(StoreOptions options, string name, string args)
        {
            var function = new SystemFunction(options, name, args);
            _systemFunctions.Add(name, function);
        }

        public IEnumerable<Type> DependentTypes()
        {
            yield break;
        }

        public bool IsActive { get; } = true;
        public ISchemaObject[] Objects => _systemFunctions.Values.OfType<ISchemaObject>().ToArray();
        public Type StorageType { get; } = typeof(SystemFunctions);
        public string Identifier { get; } = "system_functions";
        public void WritePermissions(DdlRules rules, StringWriter writer)
        {
            // Nothing
        }
    }
}
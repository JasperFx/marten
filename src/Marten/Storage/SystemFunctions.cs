using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Weasel.Postgresql;

namespace Marten.Storage
{
    internal class SystemFunctions: IFeatureSchema
    {
        private readonly StoreOptions _options;
        private readonly IDictionary<string, SystemFunction> _systemFunctions = new Dictionary<string, SystemFunction>();
        private readonly bool _isActive = true;

        public SystemFunctions(StoreOptions options)
        {
            _options = options;
        }

        public void AddSystemFunction(StoreOptions options, string name, string args)
        {
            var function = new SystemFunction(options, name, args);

            if (!_systemFunctions.ContainsKey(name))
            {
                _systemFunctions[name] = function;
            }
        }

        public IEnumerable<Type> DependentTypes()
        {
            yield break;
        }

        public bool IsActive(StoreOptions options)
        {
            return _isActive;
        }

        public ISchemaObject[] Objects => _systemFunctions.Values.OfType<ISchemaObject>().ToArray();
        public Type StorageType { get; } = typeof(SystemFunctions);
        public string Identifier { get; } = "system_functions";

        public void WritePermissions(DdlRules rules, TextWriter writer)
        {
            // Nothing
        }
    }
}

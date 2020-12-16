using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.Schema;

namespace Marten.Storage
{
    public class SystemFunctions: IFeatureSchema
    {
        private readonly IDictionary<string, SystemFunction> _systemFunctions = new Dictionary<string, SystemFunction>();
        private readonly bool _isActive = true;

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

        public void WritePermissions(DdlRules rules, StringWriter writer)
        {
            // Nothing
        }
    }
}

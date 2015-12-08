using System;
using System.Reflection;

namespace Marten.Schema
{
    public abstract class StorageArgument
    {
        public string Name { get; set; }
        public Type ArgType { get; set; }

        public StorageArgument(string name, Type argType)
        {
            Name = name;
            ArgType = argType;
        }

        public abstract object GetValue(IDocumentSchema schema);

        public string ToCtorArgument()
        {
            return $"{ArgType.FullName} {Name}";
        }

        public string ToCtorLine()
        {
            return $"_{Name} = {Name};";
        }

        public string ToFieldDeclaration()
        {
            return $"private readonly {ArgType.FullName} _{Name};";
        }
    }
}
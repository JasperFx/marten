using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace Marten.Codegen
{
    [Serializable]
    public class AssemblyReferenceException : Exception
    {
        public AssemblyReferenceException(Assembly assembly, Exception innerException) : base($"Unable to create a MetadataReference for {assembly.FullName}", innerException)
        {
        }

        protected AssemblyReferenceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
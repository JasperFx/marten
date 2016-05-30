using System;
using System.Runtime.Serialization;
using Baseline;

namespace Marten.Schema
{
    [Serializable]
    public class MartenSchemaException : Exception
    {
        public MartenSchemaException(object subject, string ddl, Exception inner) : base($"DDL Execution for '{subject}' Failed!\n\n{ddl}", inner)
        {
        }

        protected MartenSchemaException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
using System;
using System.Runtime.Serialization;
using Baseline;

namespace Marten.Schema
{
    [Serializable]
    public class MartenSchemaException : Exception
    {
        public MartenSchemaException(string ddl, Exception inner) : base($"DDL Execution Failed!\n\n{ddl}", inner)
        {
        }

        public MartenSchemaException(Type documentType, string ddl, Exception inner) : base($"DDL Execution Failed for document type {documentType.GetFullName()}!\n\n{ddl}", inner)
        {
        }

        protected MartenSchemaException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
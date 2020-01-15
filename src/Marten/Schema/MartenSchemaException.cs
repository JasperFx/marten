using System;

namespace Marten.Schema
{
#if SERIALIZE
    [Serializable]
#endif

    public class MartenSchemaException: Exception
    {
        public MartenSchemaException(object subject, string ddl, Exception inner) : base($"DDL Execution for '{subject}' Failed!\n\n{ddl}", inner)
        {
        }

#if SERIALIZE
        protected MartenSchemaException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}

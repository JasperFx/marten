using System;

namespace Marten.Exceptions;
#if SERIALIZE
    [Serializable]
#endif

public class MartenSchemaException: MartenException
{
    public MartenSchemaException(object subject, string ddl, Exception inner): base(
        $"DDL Execution for '{subject}' Failed!\n\n{ddl}", inner)
    {
    }

#if SERIALIZE
        protected MartenSchemaException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
}

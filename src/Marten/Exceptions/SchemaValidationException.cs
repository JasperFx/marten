using System;

namespace Marten.Exceptions
{
    public class SchemaValidationException: Exception
    {
        public SchemaValidationException(string ddl)
            : base("Configuration to Schema Validation Failed! These changes detected:\n\n" + ddl)
        {
        }

#if SERIALIZE
        protected SchemaValidationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}

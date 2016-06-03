using System;
using System.Runtime.Serialization;

namespace Marten.Schema
{
#if SERIALIZE
    [Serializable]
#endif
    public class InvalidDocumentException : Exception
    {
        public InvalidDocumentException(string message) : base(message)
        {
        }

#if SERIALIZE
        protected InvalidDocumentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
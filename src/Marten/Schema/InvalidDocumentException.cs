using System;
using System.Runtime.Serialization;

namespace Marten.Schema
{
    [Serializable]
    public class InvalidDocumentException : Exception
    {
        public InvalidDocumentException(string message) : base(message)
        {
        }

        protected InvalidDocumentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
namespace Marten.Exceptions;
#if SERIALIZE
    [Serializable]
#endif

public class InvalidDocumentException: MartenException
{
    public InvalidDocumentException(string message): base(message)
    {
    }

#if SERIALIZE
        protected InvalidDocumentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
}

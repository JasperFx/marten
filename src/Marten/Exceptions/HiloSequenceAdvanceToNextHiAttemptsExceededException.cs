using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class HiloSequenceAdvanceToNextHiAttemptsExceededException : Exception
    {
        private const string message = "Advance to next hilo sequence retry limit exceeded. Unable to secure next hi sequence";
        public HiloSequenceAdvanceToNextHiAttemptsExceededException() : base(message)
        { }

        protected HiloSequenceAdvanceToNextHiAttemptsExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}

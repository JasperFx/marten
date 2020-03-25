using System;
namespace Marten.Schema.Identity.Sequences
{
    public class HiloSequenceAdvanceToNextHiAttemptsExceededException : Exception
    {
        private const string message = "Advance to next hilo sequence retry limit exceeded. Unable to secure next hi sequence";
        public HiloSequenceAdvanceToNextHiAttemptsExceededException() : base(message)
        { }
    }
}

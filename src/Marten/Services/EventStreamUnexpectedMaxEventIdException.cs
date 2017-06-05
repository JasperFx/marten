using System;

namespace Marten.Services
{
    public class EventStreamUnexpectedMaxEventIdException : Exception
    {
        public EventStreamUnexpectedMaxEventIdException(int expected, int actual) : base($"Unexpected MAX(id) for event stream, expected {expected} but got {actual}")
        {
            
        }
    }
}
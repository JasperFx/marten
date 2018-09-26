using System;

namespace Marten.Services
{
    public class EventStreamUnexpectedMaxEventIdException : Exception
    {
        public EventStreamUnexpectedMaxEventIdException(object identifier, int expected, int actual) : base($"Unexpected MAX(id) for event stream with identifier {identifier}, expected {expected} but got {actual}")
        {
            
        }
    }
}
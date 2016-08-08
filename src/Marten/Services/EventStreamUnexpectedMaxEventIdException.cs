using System;
using Marten.Services.Events;

namespace Marten.Services
{
    public class EventStreamUnexpectedMaxEventIdException : Exception
    {
        public EventStreamUnexpectedMaxEventIdException(Exception inner) : base(EventContracts.UnexpectedMaxEventIdForStream, inner)
        {
            
        }
    }
}
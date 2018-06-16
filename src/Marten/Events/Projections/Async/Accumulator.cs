using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Projections.Async
{
    public class Accumulator
    {
        public EventPage First { get; private set; }

        public EventPage Last { get; private set; }

        public int CachedEventCount
        {
            get { return AllPages().Sum(x => x.Count); }
        }

        public IEnumerable<EventPage> AllPages()
        {
            var page = First;
            while (page != null)
            {
                yield return page;
                page = page.Next;
            }
        }

        public void Store(EventPage page)
        {
            if (page.Count == 0) return;
            
            if (First == null)
            {
                First = page;
                Last = page;
            }
            else if (Last != null)
            {
                Last.Next = page;
                Last = page;
            }
        }

        public void Prune(long eventFloor)
        {
            while (First != null && First.To <= eventFloor)
            {
                First = First.Next;
            }
        }

    }
}
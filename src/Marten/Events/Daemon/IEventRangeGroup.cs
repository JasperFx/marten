using System;

namespace Marten.Events.Daemon
{
    internal interface IEventRangeGroup: IDisposable
    {
        EventRange Range { get; }

        /// <summary>
        /// Teardown any existing state. Used to clean off existing work
        /// before doing retries
        /// </summary>
        void Reset();
    }
}

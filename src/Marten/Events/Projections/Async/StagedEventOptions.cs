using System;

namespace Marten.Events.Projections.Async
{
    public class StagedEventOptions
    {
        public string Name { get; set; } = Guid.NewGuid().ToString();
        public int PageSize { get; set; } = 100;
        public string[] EventTypeNames { get; set; } = new string[0];
    }
}
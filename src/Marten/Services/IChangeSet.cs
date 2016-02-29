using System.Collections.Generic;

namespace Marten.Services
{
    public interface IChangeSet
    {
        IEnumerable<object> Updated { get; } 
        IEnumerable<object> Inserted { get; }
        IEnumerable<Delete> Deleted { get; }


         
    }
}
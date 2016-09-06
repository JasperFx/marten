using System.Collections.Generic;
using System.Reflection;

namespace Marten.Linq.Compiled
{
    public interface IDictionaryElement<TQuery>
    {
        void Write(TQuery target, IDictionary<string, object> dictionary);
        MemberInfo Member { get; }
    }
}
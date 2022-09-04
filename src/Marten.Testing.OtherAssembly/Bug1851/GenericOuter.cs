using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;

namespace Marten.Testing.OtherAssembly.Bug1851;

public class GenericOuter<TInOtherAssembly> where TInOtherAssembly : StoredObjectInOtherAssembly
{
    public class FindByNameQuery: ICompiledQuery<TInOtherAssembly, string>
    {
        public string Name { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<TInOtherAssembly>, string>> QueryIs()
        {
            return q => q.Where(x => x.Name == Name).Select(x => x.Name).FirstOrDefault();
        }
    }
}
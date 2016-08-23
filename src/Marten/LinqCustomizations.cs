using System.Collections.Generic;
using Marten.Linq.Parsing;

namespace Marten
{
    public class LinqCustomizations
    {
        /// <summary>
        ///     Add custom Linq expression parsers for your own methods
        /// </summary>
        public readonly IList<IMethodCallParser> MethodCallParsers = new List<IMethodCallParser>();
    }
}
using System.Collections.Generic;
using System.Data.Common;
using Marten.Services;

namespace Marten.Linq
{
    public class StringSelector : ISelector<string>
    {
        private static string[] _empty = {};
        private List<string> _results = new List<string>();

        public string Resolve(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetString(0);
        }

        public string[] SelectFields()
        {
            return _empty;
        }
    }
}
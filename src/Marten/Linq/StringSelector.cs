using System.Collections.Generic;
using System.Data.Common;
using Marten.Services;

namespace Marten.Linq
{
    public class StringSelector : BasicSelector, ISelector<string>
    {
        public StringSelector() : base("data")
        {
        }

        public string Resolve(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetString(0);
        }
    }
}
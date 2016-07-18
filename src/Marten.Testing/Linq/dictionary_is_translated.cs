using Marten.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Marten.Testing.Linq
{
    public class dictionary_is_translated : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void dictionary_containskey_is_translated_to_json_map()
        {
            var query = theSession.Query<Target>().Where(t => t.StringDict.ContainsKey("foo"));
            var command = query.ToCommand(Marten.Linq.FetchType.FetchMany);
            var dictParam = command.Parameters[0];
            dictParam.DbType.ShouldBeTheSameAs(System.Data.DbType.Object);
            dictParam.Value.ShouldBeTheSameAs(new Dictionary<string, string> { { "foo", null } });
        }

        [Fact]
        public void icollection_keyvaluepair_contains_is_translated_to_json_map()
        {
            var query = theSession.Query<Target>().Where(t => t.StringDict.Contains(new KeyValuePair<string, string>("foo", "bar")));
            var command = query.ToCommand(Marten.Linq.FetchType.FetchMany);
            var dictParam = command.Parameters[0];
            dictParam.DbType.ShouldBeTheSameAs(System.Data.DbType.Object);
            dictParam.Value.ShouldBeTheSameAs(new Dictionary<string, string> { { "foo", "bar" } });
        }

        [Fact]
        public void ienumerable_keyvaluepair_contains_is_translated_to_json_map()
        {
            var query = theSession.Query<Target>().Where(t => ((IEnumerable<KeyValuePair<string,string>>) t.StringDict).Contains(new KeyValuePair<string, string>("foo", "bar")));
            var command = query.ToCommand(Marten.Linq.FetchType.FetchMany);
            var dictParam = command.Parameters[0];
            dictParam.DbType.ShouldBeTheSameAs(System.Data.DbType.Object);
            dictParam.Value.ShouldBeTheSameAs(new Dictionary<string, string> { { "foo", "bar" } });
        }
    }
}

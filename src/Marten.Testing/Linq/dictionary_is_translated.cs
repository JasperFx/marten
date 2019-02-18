using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class dictionary_is_translated : DocumentSessionFixture<NulloIdentityMap>
    {
        public dictionary_is_translated()
        {
            theStore.BulkInsert(Target.GenerateRandomData(100).ToArray());
        }

        [Fact]
        public void dictionary_containskey_is_translated_to_json_map()
        {
            var query = theSession.Query<Target>().Where(t => t.StringDict.ContainsKey("foo"));
            var command = query.ToCommand(Marten.Linq.FetchType.FetchMany);
            var dictParam = command.Parameters[0];
            (dictParam.DbType == System.Data.DbType.String).ShouldBeTrue();
            (dictParam.Value.ToString() == "foo").ShouldBeTrue();
        }

        // using key0 and value0 for these because the last node, which is deep, should have at least a single dict node

        [Fact]
        public void dict_can_query_using_containskey()
        {
            var results = theSession.Query<Target>().Where(x => x.StringDict.ContainsKey("key0")).ToList();
            results.All(r => r.StringDict.ContainsKey("key0")).ShouldBeTrue();
        }

        [Fact]
        public void dict_can_query_using_containsKVP()
        {
            var kvp = new KeyValuePair<string, string>("key0", "value0");
            var results = theSession.Query<Target>().Where(x => x.StringDict.Contains(kvp)).ToList();
            results.All(r => r.StringDict.Contains(kvp)).ShouldBeTrue();
        }

        [Fact]
        public void icollection_keyvaluepair_contains_is_translated_to_json_map()
        {
            var query = theSession.Query<Target>().Where(t => t.StringDict.Contains(new KeyValuePair<string, string>("foo", "bar")));
            var command = query.ToCommand(Marten.Linq.FetchType.FetchMany);
            var dictParam = command.Parameters[0];
            (dictParam.NpgsqlDbType == NpgsqlTypes.NpgsqlDbType.Jsonb).ShouldBeTrue();
            (dictParam.Value.ToString() == "{\"foo\":\"bar\"}").ShouldBeTrue();
        }
    }
}
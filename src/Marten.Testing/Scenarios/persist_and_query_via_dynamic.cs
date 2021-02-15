using System.IO;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Scenarios
{
    public class persist_and_query_via_dynamic: IntegrationContext
    {
        #region sample_sample-scenarios-dynamic-type
        public class TemperatureData
        {
            public int Id { get; set; }
            public dynamic Values { get; set; }
        }
        #endregion sample_sample-scenarios-dynamic-type

        [Fact]
        public void CanPersistAndQueryDynamic()
        {
            #region sample_sample-scenarios-dynamic-records
            // Our documents with non-uniform structure
            var jsonRecords = @"
[{
        'sensor': 'aisle-1',
        'timestamp': '2020-01-21 11:19:19.283',
        'temperature': 21.2
    }, {
        'sensor': 'aisle-2',
        'timestamp': '2020-01-21 11:18:19.220',
        'temperature': 21.6
    }, {
        'sensor': 'aisle-1',
        'timestamp': '2020-01-21 11:17:19.190',
        'temperature': 21.6
    }, {
        'detector': 'aisle-1',
        'timestamp': '2020-01-21 11:16:19.100',
        'temperature': 20.9
    }, {
        'sensor': 'aisle-3',
        'timestamp': '2020-01-21 11:15:19.037',
        'temperature': 21.7,
    }, {
        'detector': 'aisle-1',
        'timestamp': '2020-01-21 11:14:19.100',
        'temperature': -1.0
    }
]";
            #endregion sample_sample-scenarios-dynamic-records

            #region sample_sample-scenarios-dynamic-insertandquery
            using (var reader = new StringReader(jsonRecords))
            {
                // Deserialize our document
                var records = theStore.Serializer.FromJson<dynamic[]>(reader);
                var docs = records.Select(x => new TemperatureData { Values = x }).ToArray();

                // Persist our records
                theStore.BulkInsertDocuments(docs);

                using (var session = theStore.OpenSession())
                {
                    // Read back the data for "aisle-1"
                    dynamic[] tempsFromDb = session.Query(typeof(TemperatureData),
                        "where data->'Values'->>'detector' = :sensor OR data->'Values'->>'sensor' = :sensor", new { sensor = "aisle-1" }).ToArray();

                    var temperatures = tempsFromDb.Select(x => (decimal)x.Values.temperature);

                    Assert.Equal(15.675m, temperatures.Average());
                    Assert.Equal(4, tempsFromDb.Length);
                }
            }
            #endregion sample_sample-scenarios-dynamic-insertandquery
        }

        public persist_and_query_via_dynamic(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}

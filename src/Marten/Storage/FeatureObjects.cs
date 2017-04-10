using Marten.Schema;
using Npgsql;

namespace Marten.Storage
{
    public class FeatureObjects 
    {
        private readonly StoreOptions _options;
        private readonly IFeatureSchema _feature;

        public FeatureObjects(StoreOptions options, IFeatureSchema feature)
        {
            _options = options;
            _feature = feature;
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, NpgsqlConnection connection,
            SchemaPatch patch)
        {
            // TODO -- validate the identifiers against the maximum allowable length

            throw new System.NotImplementedException();
        }

        public void WriteSchemaObjects(SchemaPatch patch)
        {
            // TODO -- validate the identifiers against the maximum allowable length
            throw new System.NotImplementedException();
        }

        public void RemoveSchemaObjects(SchemaPatch patch)
        {
            throw new System.NotImplementedException();
        }

        public void WritePatch(SchemaPatch patch)
        {
            throw new System.NotImplementedException();
        }

        public string Name => _feature.Identifier;
    }
}
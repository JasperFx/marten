using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Marten.Generation;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    // TODO -- might decide to get rid of this one
    public interface ISchemaObjects
    {
        void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, NpgsqlConnection connection, SchemaPatch patch);

        void WriteSchemaObjects(IDocumentSchema schema, SchemaPatch patch);

        void RemoveSchemaObjects(IManagedConnection connection);

        void ResetSchemaExistenceChecks();

        void WritePatch(IDocumentSchema schema, SchemaPatch patch);

        string Name { get; }
    }

    public interface IFeatureSchema
    {
        IFeatureSchema FindDependencies();

        bool IsActive { get; }
        string Identifier { get; }
        IEnumerable<ISchemaObject> Objects { get; }
    }

    public class FeatureObjects : ISchemaObjects
    {
        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, NpgsqlConnection connection,
            SchemaPatch patch)
        {
            throw new System.NotImplementedException();
        }

        public void WriteSchemaObjects(IDocumentSchema schema, SchemaPatch patch)
        {
            throw new System.NotImplementedException();
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new System.NotImplementedException();
        }

        public void ResetSchemaExistenceChecks()
        {
            throw new System.NotImplementedException();
        }

        public void WritePatch(IDocumentSchema schema, SchemaPatch patch)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }

    public enum SchemaPatchDifference
    {
        None,
        Create,
        Update,
        Invalid
    }

    public interface ISchemaObject
    {
        void WriteAll(SchemaPatch patch);

        string Identifier { get; }
        void WriteFetchCommand(CommandBuilder builder);

        void CreatePatch(DbDataReader reader, SchemaPatch patch);
    }

    public class Table : IEnumerable<TableColumn>
    {
        public readonly IList<TableColumn> _columns = new List<TableColumn>();

        public DbObjectName Name { get; }

        public Table(DbObjectName name)
        {
            Name = name;
        }

        public void AddPrimaryKey(TableColumn column)
        {
            PrimaryKey = column;
            column.Directive = $"CONSTRAINT pk_{Name.Name} PRIMARY KEY";
            _columns.Add(column);
        }

        public TableColumn PrimaryKey { get; private set; }

        public void AddColumn<T>() where T : TableColumn, new()
        {
            _columns.Add(new T());
        }

        public void AddColumn(TableColumn column)
        {
            _columns.Add(column);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<TableColumn> GetEnumerator()
        {
            return _columns.GetEnumerator();
        }
    }
}
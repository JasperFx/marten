using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Generation;
using Marten.Services;
using Marten.Util;

namespace Marten.Schema
{
    public class DocumentSchemaObjects : IDocumentSchemaObjects
    {
        public static DocumentSchemaObjects For<T>()
        {
            return new DocumentSchemaObjects(DocumentMapping.For<T>());
        }

        private readonly DocumentMapping _mapping;
        private bool _hasCheckedSchema;
        private readonly object _lock = new object();

        public DocumentSchemaObjects(DocumentMapping mapping)
        {
            _mapping = mapping;
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            var table = ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            new UpsertFunction(_mapping).WriteFunctionSql(schema?.StoreOptions?.UpsertType ?? PostgresUpsertType.Legacy,
                writer);

            _mapping.ForeignKeys.Each(x =>
            {
                writer.WriteLine();
                writer.WriteLine((string) x.ToDDL());
            });

            _mapping.Indexes.Each(x =>
            {
                writer.WriteLine();
                writer.WriteLine(x.ToDDL());
            });

            writer.WriteLine();
            writer.WriteLine();
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            _hasCheckedSchema = false;

            connection.Execute($"DROP TABLE IF EXISTS {_mapping.Table.QualifiedName} CASCADE;");

            RemoveUpsertFunction(connection);
        }

        /// <summary>
        ///     Only for testing scenarios
        /// </summary>
        /// <param name="connection"></param>
        public void RemoveUpsertFunction(IManagedConnection connection)
        {
            var dropTargets = DocumentCleaner.DropFunctionSql.ToFormat(_mapping.UpsertFunction.Name, _mapping.UpsertFunction.Schema);

            var drops = connection.GetStringList(dropTargets);
            drops.Each(drop => connection.Execute(drop));
        }

        public void ResetSchemaExistenceChecks()
        {
            _hasCheckedSchema = false;
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema,
            Action<string> executeSql)
        {
            if (_hasCheckedSchema) return;


            var diff = CreateSchemaDiff(schema);

            if (!diff.HasDifferences())
            {
                _hasCheckedSchema = true;
                return;
            }

            lock (_lock)
            {
                if (_hasCheckedSchema) return;

                buildOrModifySchemaObjects(diff, autoCreateSchemaObjectsMode, schema, executeSql);

                _hasCheckedSchema = true;
            }
        }

        public SchemaDiff CreateSchemaDiff(IDocumentSchema schema)
        {
            var objects = schema.DbObjects.FindSchemaObjects(_mapping);
            return new SchemaDiff(schema, objects, _mapping);
        }

        private void buildOrModifySchemaObjects(SchemaDiff diff, AutoCreate autoCreateSchemaObjectsMode,
            IDocumentSchema schema, Action<string> executeSql)
        {
            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                var className = nameof(StoreOptions);
                var propName = nameof(StoreOptions.AutoCreateSchemaObjects);

                string message =
                    $"No document storage exists for type {_mapping.DocumentType.FullName} and cannot be created dynamically unless the {className}.{propName} = true. See http://jasperfx.github.io/marten/documentation/documents/ for more information";
                throw new InvalidOperationException(message);
            }

            if (diff.AllMissing)
            {
                rebuildTableAndUpsertFunction(schema, executeSql);
                return;
            }

            if (autoCreateSchemaObjectsMode == AutoCreate.CreateOnly)
            {
                throw new InvalidOperationException(
                    $"The table for document type {_mapping.DocumentType.FullName} is different than the current schema table, but AutoCreateSchemaObjects = '{nameof(AutoCreate.CreateOnly)}'");
            }

            if (diff.CanPatch())
            {
                diff.CreatePatch(executeSql);
            }
            else if (autoCreateSchemaObjectsMode == AutoCreate.All)
            {
                // TODO -- better evaluation here against the auto create mode
                rebuildTableAndUpsertFunction(schema, executeSql);
            }
            else
            {
                throw new InvalidOperationException(
                    $"The table for document type {_mapping.DocumentType.FullName} is different than the current schema table, but AutoCreateSchemaObjects = '{autoCreateSchemaObjectsMode}'");
            }
        }

        private void rebuildTableAndUpsertFunction(IDocumentSchema schema, Action<string> executeSql)
        {
            var writer = new StringWriter();
            WriteSchemaObjects(schema, writer);

            var sql = writer.ToString();
            executeSql(sql);
        }


        public virtual TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
                                                                       // can do foreign keys
        {
            var pgIdType = TypeMappings.GetPgType(_mapping.IdMember.GetMemberType());
            var table = new TableDefinition(_mapping.Table, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb") { Directive = "NOT NULL" });

            table.Columns.Add(new TableColumn(DocumentMapping.LastModifiedColumn, "timestamp with time zone")
            {
                Directive = "DEFAULT transaction_timestamp()"
            });
            table.Columns.Add(new TableColumn(DocumentMapping.VersionColumn, "uuid"));
            table.Columns.Add(new TableColumn(DocumentMapping.DotNetTypeColumn, "varchar"));

            _mapping.DuplicatedFields.Select(x => x.ToColumn(schema)).Each(x => table.Columns.Add(x));


            if (_mapping.IsHierarchy())
            {
                table.Columns.Add(new TableColumn(DocumentMapping.DocumentTypeColumn, "varchar"));
            }

            return table;
        }
    }

}
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using FubuCore;
using FubuCore.Csv;
using Marten.Codegen;
using Marten.Generation;
using Marten.Generation.Templates;
using Marten.Util;

namespace Marten.Schema
{
    public class DocumentMapping
    {
        public DocumentMapping(Type documentType)
        {
            DocumentType = documentType;

            IdMember = (MemberInfo) documentType.GetProperties().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"))
                ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"));

            TableName = TableNameFor(documentType);

            UpsertName = UpsertNameFor(documentType);
        }

        public string UpsertName { get; private set; }

        public Type DocumentType { get; private set; }

        public string TableName { get; set; }

        public MemberInfo IdMember { get; set; }

        // LATER?
        public IdStrategy IdStrategy { get; set; }

        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name.ToLower();
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name.ToLower();
        }

        public TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
            // can do foreign keys
        {
            // TODO -- blow up if no IdMember or no TableName

            var pgIdType = TypeMappings.PgTypes[IdMember.GetMemberType()];
            var table = new TableDefinition(TableName, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb NOT NULL"));

            return table;

        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            var table = ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            var pgIdType = TypeMappings.PgTypes[IdMember.GetMemberType()];

            var sql = TemplateSource.UpsertDocument()
                .Replace("%TABLE_NAME%", TableName)
                .Replace("%SPROC_NAME%", UpsertName)
                .Replace("%ID_TYPE%", pgIdType);

            writer.WriteLine(sql);

            writer.WriteLine();
            writer.WriteLine();
        }

        public void GenerateDocumentStorage(SourceWriter writer)
        {
            writer.Write($@"
BLOCK:public class {DocumentType.Name}Storage : IDocumentStorage
public Type DocumentType => typeof ({DocumentType.Name});

public string TableName {{ get; }} = `{TableName}`;

BLOCK:public NpgsqlCommand UpsertCommand(object document, string json)
return UpsertCommand(({DocumentType.Name})document, json);
END

BLOCK:public NpgsqlCommand LoaderCommand(object id)
return new NpgsqlCommand(`select data from {TableName} where id = :id`).WithParameter(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForId(object id)
return new NpgsqlCommand(`delete from {TableName} where id = :id`).WithParameter(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForEntity(object entity)
return DeleteCommandForId((({DocumentType.Name})entity).{IdMember.Name});
END

BLOCK:public NpgsqlCommand LoadByArrayCommand<T>(T[] ids)
return new NpgsqlCommand(`select data from {TableName} where id = ANY(:ids)`).WithParameter(`ids`, ids);
END

BLOCK:public NpgsqlCommand AnyCommand(QueryModel queryModel)
return new DocumentQuery<{DocumentType.Name}>(`{TableName}`, queryModel).ToAnyCommand();
END

BLOCK:public NpgsqlCommand CountCommand(QueryModel queryModel)
return new DocumentQuery<{DocumentType.Name}>(`{TableName}`, queryModel).ToCountCommand();
END

// TODO: This wil need to get fancier later
BLOCK:public NpgsqlCommand UpsertCommand({DocumentType.Name} document, string json)
return new NpgsqlCommand(`{UpsertName}`)
    .AsSproc()
    .WithParameter(`id`, document.{IdMember.Name})
    .WithJsonParameter(`doc`, json);
END

END

");

        }
    }

    // There would be others for sequences and hilo, etc.
    public interface IdStrategy
    {
    }

    public class AssignGuid : IdStrategy
    {
    }

    public enum DuplicatedFieldRole
    {
        Search,
        ForeignKey
    }

    public class DuplicatedField
    {
        public DuplicatedField(MemberInfo[] memberPath)
        {
            MemberPath = memberPath;
            UpsertArgument = new UpsertArgument();
        }

        /// <summary>
        ///     Because this could be a deeply nested property and maybe even an
        ///     indexer? Or change to MemberInfo[] instead.
        /// </summary>
        public MemberInfo[] MemberPath { get; private set; }

        public string ColumnName { get; set; }

        public DuplicatedFieldRole Role { get; set; } = DuplicatedFieldRole.Search;

        public UpsertArgument UpsertArgument { get; private set; }

        // I say you don't need a ForeignKey 
        public virtual ColumnDefinition ToColumn()
        {
            throw new NotImplementedException();
        }
    }

    public class UpsertArgument
    {
        public string Name { get; set; }
        public string PostgresType { get; set; }
    }
}
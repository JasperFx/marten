using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FubuCore;
using FubuCore.Reflection;
using Marten.Codegen;
using Marten.Generation;
using Marten.Util;

namespace Marten.Schema
{
    // TODO - This is becoming a Blob class. Split out the Sql and C# generation maybe?
    public class DocumentMapping
    {
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private PropertySearching _propertySearching = PropertySearching.JSONB_To_Record;

        public DocumentMapping(Type documentType)
        {
            DocumentType = documentType;

            IdMember = (MemberInfo) documentType.GetProperties().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"))
                       ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"));

            TableName = TableNameFor(documentType);

            UpsertName = UpsertNameFor(documentType);

            documentType.GetProperties().Where(x => TypeMappings.HasTypeMapping(x.PropertyType)).Each(prop =>
            {

                var field = new LateralJoinField(prop);
                _fields[field.MemberName] = field;

                prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop));
            });

            documentType.GetFields().Where(x => TypeMappings.HasTypeMapping(x.FieldType)).Each(fieldInfo =>
            {
                var field = new LateralJoinField(fieldInfo);
                _fields.AddOrUpdate(field.MemberName, field, (key, f) => f);

                fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo));
            });
        }

        public string UpsertName { get; }

        public Type DocumentType { get; }

        public string TableName { get; set; }

        public MemberInfo IdMember { get; set; }

        public PropertySearching PropertySearching
        {
            get { return _propertySearching; }
            set
            {
                _propertySearching = value;

                if (_propertySearching == PropertySearching.JSONB_To_Record)
                {
                    var fields = _fields.Values.Where(x => x.Members.Length == 1).OfType<JsonLocatorField>().ToArray();
                    fields.Each(x => { _fields[x.MemberName] = new LateralJoinField(x.Members.Last()); });
                }
                else
                {
                    var fields = _fields.Values.Where(x => x.Members.Length == 1).OfType<LateralJoinField>().ToArray();
                    fields.Each(x => { _fields[x.MemberName] = new JsonLocatorField(x.Members.Last()); });
                }
            }
        }

        public IEnumerable<DuplicatedField> DuplicatedFields => _fields.Values.OfType<DuplicatedField>();

        public static string TableNameFor(Type documentType)
        {
            return "mt_doc_" + documentType.Name.ToLower();
        }

        public static string UpsertNameFor(Type documentType)
        {
            return "mt_upsert_" + documentType.Name.ToLower();
        }

        public IField FieldFor(MemberInfo member)
        {
            return _fields.GetOrAdd(member.Name, name =>
            {
                return PropertySearching == PropertySearching.JSONB_To_Record
                    ? (IField) new LateralJoinField(member)
                    : new JsonLocatorField(member);
            });
        }

        public IField FieldFor(string memberName)
        {
            return _fields[memberName];
        }

        public TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
            // can do foreign keys
        {
            // TODO -- blow up if no IdMember or no TableName

            var pgIdType = TypeMappings.PgTypes[IdMember.GetMemberType()];
            var table = new TableDefinition(TableName, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb NOT NULL"));

            _fields.Values.OfType<DuplicatedField>().Select(x => x.ToColumn(schema)).Each(x => table.Columns.Add(x));


            return table;
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            var table = ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            var pgIdType = TypeMappings.PgTypes[IdMember.GetMemberType()];

            var args = new List<UpsertArgument>
            {
                new UpsertArgument {Arg = "docId", PostgresType = pgIdType},
                new UpsertArgument {Arg = "doc", PostgresType = "JSON"}
            };

            var duplicates = DuplicatedFields.Select(x => x.UpsertArgument).ToArray();
            args.AddRange(duplicates);

            var argList = args.Select(x => x.ArgumentDeclaration()).Join(", ");
            var valueList = args.Select(x => x.Arg).Join(", ");

            var updates = "data = doc";
            if (duplicates.Any())
            {
                updates += ", " + duplicates.Select(x => $"{x.Column} = {x.Arg}").Join(", ");
            }


            writer.WriteLine($"CREATE OR REPLACE FUNCTION {UpsertName}({argList}) RETURNS VOID AS");
            writer.WriteLine("$$");
            writer.WriteLine("BEGIN");
            writer.WriteLine($"INSERT INTO {TableName} VALUES ({valueList})");
            writer.WriteLine($"  ON CONFLICT ON CONSTRAINT pk_{TableName}");
            writer.WriteLine($"  DO UPDATE SET {updates};");
            writer.WriteLine("END;");
            writer.WriteLine("$$ LANGUAGE plpgsql;");


            writer.WriteLine();
            writer.WriteLine();
        }

        public void GenerateDocumentStorage(SourceWriter writer)
        {
            var extraUpsertArguments = DuplicatedFields.Any()
                ? DuplicatedFields.Select(x => x.WithParameterCode()).Join("")
                : "";


            writer.Write(
                $@"
BLOCK:public class {DocumentType.Name
                    }Storage : IDocumentStorage
public Type DocumentType => typeof ({DocumentType.Name
                    });

BLOCK:public NpgsqlCommand UpsertCommand(object document, string json)
return UpsertCommand(({
                    DocumentType.Name
                    })document, json);
END

BLOCK:public NpgsqlCommand LoaderCommand(object id)
return new NpgsqlCommand(`select data from {
                    TableName
                    } where id = :id`).WithParameter(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForId(object id)
return new NpgsqlCommand(`delete from {
                    TableName
                    } where id = :id`).WithParameter(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForEntity(object entity)
return DeleteCommandForId((({
                    DocumentType.Name})entity).{IdMember.Name
                    });
END

BLOCK:public NpgsqlCommand LoadByArrayCommand<T>(T[] ids)
return new NpgsqlCommand(`select data from {
                    TableName
                    } where id = ANY(:ids)`).WithParameter(`ids`, ids);
END


// TODO: This wil need to get fancier later
BLOCK:public NpgsqlCommand UpsertCommand({
                    DocumentType.Name} document, string json)
return new NpgsqlCommand(`{UpsertName
                    }`)
    .AsSproc()
    .WithParameter(`id`, document.{IdMember.Name
                    })
    .WithJsonParameter(`doc`, json){extraUpsertArguments};
END

END

");
        }

        public static DocumentMapping For<T>()
        {
            return new DocumentMapping(typeof (T));
        }

        public DuplicatedField DuplicateField(string memberName)
        {
            var field = FieldFor(memberName);
            var duplicate = new DuplicatedField(field.Members);

            _fields[memberName] = duplicate;

            return duplicate;
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            if (members.Count() == 1)
            {
                return FieldFor(members.Single());
            }

            var key = members.Select(x => x.Name).Join("");
            return _fields.GetOrAdd(key, _ =>
            {
                return new JsonLocatorField(members.ToArray());
            });
        }
    }
}
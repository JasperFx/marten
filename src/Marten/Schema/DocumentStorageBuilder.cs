using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore;
using Marten.Codegen;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq;

namespace Marten.Schema
{
    public static class DocumentStorageBuilder
    {
        public static IDocumentStorage Build(IDocumentSchema schema, Type documentType)
        {
            return Build(schema, new DocumentMapping(documentType));
        }

        public static IDocumentStorage Build(IDocumentSchema schema, DocumentMapping mapping)
        {
            return Build(schema, new[] {mapping}).Single();
        }

        public static IEnumerable<IDocumentStorage> Build(IDocumentSchema schema, DocumentMapping[] mappings)
        {
            // Generate the actual source code
            var code = GenerateDocumentStorageCode(mappings);

            var generator = new AssemblyGenerator();

            // Tell the generator which other assemblies that it should be referencing 
            // for the compilation
            generator.ReferenceAssembly(Assembly.GetExecutingAssembly());
            generator.ReferenceAssemblyContainingType<NpgsqlConnection>();
            generator.ReferenceAssemblyContainingType<QueryModel>();
            generator.ReferenceAssemblyContainingType<DbCommand>();
            generator.ReferenceAssemblyContainingType<Component>();

            mappings.Select(x => x.DocumentType.Assembly).Distinct().Each(assem => generator.ReferenceAssembly(assem));

            // build the new assembly -- this will blow up if there are any
            // compilation errors with the list of errors and the actual code

            var assembly = generator.Generate(code);

            return assembly
                .GetExportedTypes()
                .Where(x => x.IsConcreteTypeOf<IDocumentStorage>())
                .Select(x =>
                {
                    var docType =
                        x.FindInterfaceThatCloses(typeof (IdAssignment<>)).GetGenericArguments().Single();

                    var mapping = mappings.Single(m => m.DocumentType == docType);

                    var arguments = mapping.IdStrategy.ToArguments().Select(arg => arg.GetValue(schema)).ToArray();

                    var ctor = x.GetConstructors().Single();

                    return ctor.Invoke(arguments).As<IDocumentStorage>();
                });
        }

        

        public static string GenerateDocumentStorageCode(DocumentMapping[] mappings)
        {
            var writer = new SourceWriter();

            // TODO -- get rid of the magic strings
            var namespaces = new List<string> {"System", "Marten", "Marten.Schema", "Marten.Services", "Marten.Linq", "Marten.Util", "Npgsql", "Remotion.Linq", typeof(NpgsqlDbType).Namespace, typeof(IEnumerable<>).Namespace};
            namespaces.AddRange(mappings.Select(x => x.DocumentType.Namespace));

            namespaces.Distinct().OrderBy(x => x).Each(x => writer.WriteLine($"using {x};"));
            writer.BlankLine();

            writer.StartNamespace("Marten.GeneratedCode");

            mappings.Each(x =>
            {
                GenerateDocumentStorage(x, writer);
                writer.BlankLine();
                writer.BlankLine();
            });

            writer.FinishBlock();
            return writer.Code();
        }

        public static void GenerateDocumentStorage(DocumentMapping mapping, SourceWriter writer)
        {
            var extraUpsertArguments = mapping.DuplicatedFields.Any()
                ? mapping.DuplicatedFields.Select(x => x.WithParameterCode()).Join("")
                : "";


            // TODO -- move more of this into DocumentMapping, DuplicatedField, IField, etc.
            var id_NpgsqlDbType = TypeMappings.ToDbType(mapping.IdMember.GetMemberType());
            var duplicatedFieldsInBulkLoading = mapping.DuplicatedFields.Any() 
                ? ", " + mapping.DuplicatedFields.Select(x => x.ColumnName).Join(", ") 
                : string.Empty;

            var duplicatedFieldsInBulkLoadingWriter = "";
            if (mapping.DuplicatedFields.Any())
            {
                duplicatedFieldsInBulkLoadingWriter = mapping.DuplicatedFields.Select(field =>
                {
                    return field.ToBulkWriterCode();
                }).Join("\n");
            }

            

            var storageArguments = mapping.IdStrategy.ToArguments();
            var ctorArgs = storageArguments.Select(x => x.ToCtorArgument()).Join(", ");
            var ctorLines = storageArguments.Select(x => x.ToCtorLine()).Join("\n");
            var fields = storageArguments.Select(x => x.ToFieldDeclaration()).Join("\n");

            writer.Write(
                $@"
BLOCK:public class {mapping.DocumentType.Name}Storage : IDocumentStorage, IBulkLoader<{mapping.DocumentType.Name}>, IdAssignment<{mapping.DocumentType.Name}>

{fields}

BLOCK:public {mapping.DocumentType.Name}Storage({ctorArgs})
{ctorLines}
END

public Type DocumentType => typeof ({mapping.DocumentType.Name});

BLOCK:public NpgsqlCommand UpsertCommand(object document, string json)
return UpsertCommand(({mapping.DocumentType.Name})document, json);
END

BLOCK:public NpgsqlCommand LoaderCommand(object id)
return new NpgsqlCommand(`select data from {mapping.TableName} where id = :id`).With(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForId(object id)
return new NpgsqlCommand(`delete from {mapping.TableName} where id = :id`).With(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForEntity(object entity)
return DeleteCommandForId((({mapping.DocumentType.Name})entity).{mapping.IdMember.Name});
END

BLOCK:public NpgsqlCommand LoadByArrayCommand<T>(T[] ids)
return new NpgsqlCommand(`select data, id from {mapping.TableName} where id = ANY(:ids)`).With(`ids`, ids);
END


BLOCK:public NpgsqlCommand UpsertCommand({mapping.DocumentType.Name} document, string json)
return new NpgsqlCommand(`{mapping.UpsertName}`)
    .AsSproc()
    .With(`id`, document.{mapping.IdMember.Name})
    .WithJsonParameter(`doc`, json){extraUpsertArguments};
END

BLOCK:public object Assign({mapping.DocumentType.Name} document)
{mapping.IdStrategy.AssignmentBodyCode(mapping.IdMember)}
return document.{mapping.IdMember.Name};
END

BLOCK:public object Retrieve({mapping.DocumentType.Name} document)
return document.{mapping.IdMember.Name};
END

public NpgsqlDbType IdType => NpgsqlDbType.{id_NpgsqlDbType.ToString()};

BLOCK:public object Identity(object document)
return (({mapping.DocumentType})document).{mapping.IdMember.Name};
END

{toUpdateBatchMethod(mapping, id_NpgsqlDbType)}

BLOCK:public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<{mapping.DocumentType.Name}> documents)
BLOCK:using (var writer = conn.BeginBinaryImport(`COPY {mapping.TableName}(id, data{duplicatedFieldsInBulkLoading}) FROM STDIN BINARY`))
BLOCK:foreach (var x in documents)
writer.StartRow();
writer.Write(x.Id, NpgsqlDbType.{id_NpgsqlDbType});
writer.Write(serializer.ToJson(x), NpgsqlDbType.Jsonb);
{duplicatedFieldsInBulkLoadingWriter}
END
END
END

END

");
        }

        private static string toUpdateBatchMethod(DocumentMapping mapping, NpgsqlDbType idNpgsqlDbType)
        {
            var extras =
                mapping.DuplicatedFields.Select(x => x.ToUpdateBatchParam()).Join("");

            return $@"
BLOCK:public void RegisterUpdate(UpdateBatch batch, object entity)
var document = ({mapping.DocumentType})entity;
batch.Sproc(`{mapping.UpsertName}`).Param(document.{mapping.IdMember.Name}, NpgsqlDbType.{idNpgsqlDbType}).JsonEntity(document){extras};
END

BLOCK:public void RegisterUpdate(UpdateBatch batch, object entity, string json)
var document = ({mapping.DocumentType})entity;
batch.Sproc(`{mapping.UpsertName}`).Param(document.{mapping.IdMember.Name}, NpgsqlDbType.{idNpgsqlDbType}).JsonBody(json){extras};
END
";
        }
    }
}
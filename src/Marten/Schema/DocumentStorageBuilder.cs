using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
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
        public static IDocumentStorage Build(Type documentType)
        {
            return Build(new DocumentMapping(documentType));
        }

        public static IDocumentStorage Build(DocumentMapping mapping)
        {
            return Build(new[] {mapping}).Single();
        }

        public static IEnumerable<IDocumentStorage> Build(DocumentMapping[] mappings)
        {
            var code = GenerateDocumentStorageCode(mappings);

            var generator = new AssemblyGenerator();
            generator.ReferenceAssembly(Assembly.GetExecutingAssembly());
            generator.ReferenceAssemblyContainingType<NpgsqlConnection>();
            generator.ReferenceAssemblyContainingType<QueryModel>();
            generator.ReferenceAssemblyContainingType<DbCommand>();
            generator.ReferenceAssemblyContainingType<Component>();

            mappings.Select(x => x.DocumentType.Assembly).Distinct().Each(assem => generator.ReferenceAssembly(assem));

            var assembly = generator.Generate(code);

            return assembly
                .GetExportedTypes()
                .Where(x => x.IsConcreteTypeOf<IDocumentStorage>())
                .Select(x => Activator.CreateInstance(x).As<IDocumentStorage>());
        }

        

        public static string GenerateDocumentStorageCode(DocumentMapping[] mappings)
        {
            var writer = new SourceWriter();

            // TODO -- get rid of the magic strings
            var namespaces = new List<string> {"System", "Marten", "Marten.Schema", "Marten.Linq", "Marten.Util", "Npgsql", "Remotion.Linq", typeof(NpgsqlDbType).Namespace, typeof(IEnumerable<>).Namespace};
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

            writer.Write(
                $@"
BLOCK:public class {mapping.DocumentType.Name}Storage : IDocumentStorage, IBulkLoader<{mapping.DocumentType.Name}>
public Type DocumentType => typeof ({mapping.DocumentType.Name});

BLOCK:public NpgsqlCommand UpsertCommand(object document, string json)
return UpsertCommand(({mapping.DocumentType.Name})document, json);
END

BLOCK:public NpgsqlCommand LoaderCommand(object id)
return new NpgsqlCommand(`select data from {mapping.TableName} where id = :id`).WithParameter(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForId(object id)
return new NpgsqlCommand(`delete from {mapping.TableName} where id = :id`).WithParameter(`id`, id);
END

BLOCK:public NpgsqlCommand DeleteCommandForEntity(object entity)
return DeleteCommandForId((({mapping.DocumentType.Name})entity).{mapping.IdMember.Name});
END

BLOCK:public NpgsqlCommand LoadByArrayCommand<T>(T[] ids)
return new NpgsqlCommand(`select data from {mapping.TableName} where id = ANY(:ids)`).WithParameter(`ids`, ids);
END


BLOCK:public NpgsqlCommand UpsertCommand({mapping.DocumentType.Name} document, string json)
return new NpgsqlCommand(`{mapping.UpsertName}`)
    .AsSproc()
    .WithParameter(`id`, document.{mapping.IdMember.Name})
    .WithJsonParameter(`doc`, json){extraUpsertArguments};
END

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
    }
}
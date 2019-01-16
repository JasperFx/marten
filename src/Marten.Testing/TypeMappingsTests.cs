using System;
using System.Collections.Generic;
using Marten.Util;
using Npgsql;
using Npgsql.TypeHandlers;
using Npgsql.TypeMapping;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class TypeMappingsTests
    {
        [Fact]
        public void execute_to_db_type_as_int()
        {
            TypeMappings.ToDbType(typeof(int)).ShouldBe(NpgsqlDbType.Integer);
            TypeMappings.ToDbType(typeof(int?)).ShouldBe(NpgsqlDbType.Integer);
        }

        [Fact]
        public void execute_to_db_custom_mappings_resolve()
        {
            NpgsqlConnection.GlobalTypeMapper.AddMapping(new NpgsqlTypeMappingBuilder
            {
                PgTypeName = "varchar",
                NpgsqlDbType = NpgsqlDbType.Varchar,
                ClrTypes = new[] { typeof(MappedTarget) },
                TypeHandlerFactory = new TextHandlerFactory()
            }.Build());

            TypeMappings.ToDbType(typeof(MappedTarget)).ShouldBe(NpgsqlDbType.Varchar);
            ShouldThrowExtensions.ShouldThrow<Exception>(() => TypeMappings.ToDbType(typeof(UnmappedTarget)));
        }


        [Fact]
        public void execute_get_pg_type_default_mappings_resolve()
        {
            TypeMappings.GetPgType(typeof(long), EnumStorage.AsString).ShouldBe("bigint");
            TypeMappings.GetPgType(typeof(DateTime), EnumStorage.AsString).ShouldBe("timestamp without time zone");
        }

        [Fact]
        public void execute_get_pg_type_custom_mappings_resolve_or_default_to_jsonb()
        {
            NpgsqlConnection.GlobalTypeMapper.MapComposite<MappedTarget>("varchar");

            TypeMappings.GetPgType(typeof(MappedTarget), EnumStorage.AsString).ShouldBe("varchar");
            TypeMappings.GetPgType(typeof(UnmappedTarget), EnumStorage.AsString).ShouldBe("jsonb");
        }

        [Fact]
        public void execute_has_type_mapping_resolves_custom_types()
        {
            NpgsqlConnection.GlobalTypeMapper.MapComposite<MappedTarget>("varchar");

            TypeMappings.HasTypeMapping(typeof(MappedTarget)).ShouldBeTrue();
            TypeMappings.HasTypeMapping(typeof(UnmappedTarget)).ShouldBeFalse();
        }

        public class MappedTarget { }
        public class UnmappedTarget { }

        [Fact]
        public void canonicizesql_supports_tabs_as_whitespace()
        {
            var noTabsCanonized =
                "\r\nDECLARE\r\n  final_version uuid;\r\nBEGIN\r\nINSERT INTO table(\"data\", \"mt_dotnet_type\", \"id\", \"mt_version\", mt_last_modified) VALUES (doc, docDotNetType, docId, docVersion, transaction_timestamp())\r\n  ON CONFLICT ON CONSTRAINT pk_table\r\n  DO UPDATE SET \"data\" = doc, \"mt_dotnet_type\" = docDotNetType, \"mt_version\" = docVersion, mt_last_modified = transaction_timestamp();\r\n\r\n  SELECT mt_version FROM table into final_version WHERE id = docId;\r\n  RETURN final_version;\r\nEND;\r\n"
                    .CanonicizeSql();
            var tabsCanonized =
                "\r\nDECLARE\r\n\tfinal_version uuid;\r\nBEGIN\r\n\tINSERT INTO table(\"data\", \"mt_dotnet_type\", \"id\", \"mt_version\", mt_last_modified)\r\n\tVALUES (doc, docDotNetType, docId, docVersion, transaction_timestamp())\r\n\t\tON CONFLICT ON CONSTRAINT pk_table\r\n\t\t\tDO UPDATE SET \"data\" = doc, \"mt_dotnet_type\" = docDotNetType, \"mt_version\" = docVersion, mt_last_modified = transaction_timestamp();\r\n\r\n\tSELECT mt_version FROM table into final_version WHERE id = docId;\r\n\r\n\tRETURN final_version;\r\nEND;\r\n"
                    .CanonicizeSql();
            noTabsCanonized.ShouldBe(tabsCanonized);
        }

        [Fact]
        public void replaces_multiple_spaces_with_new_string()
        {
            var inputString = "Darth        Maroon the   First";
            var expectedString = "Darth Maroon the First";
            inputString.ReplaceMultiSpace(" ").ShouldBe(expectedString);
        }
    }
}

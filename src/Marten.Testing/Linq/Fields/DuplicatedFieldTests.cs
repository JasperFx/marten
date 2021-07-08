using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Services;
using Marten.Testing.Documents;
using NpgsqlTypes;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Linq.Fields
{
    public class DuplicatedFieldTests
    {
        private DuplicatedField theField;

        public DuplicatedFieldTests()
        {
            var storeOptions = new StoreOptions{};
            storeOptions.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsInteger;

            theField =
                DuplicatedField.For<User>(storeOptions, x => x.FirstName);
        }

        [Fact]
        public void create_table_column_for_non_indexed_search()
        {
            var column = theField.ToColumn();
            column.Name.ShouldBe("first_name");
            column.Type.ShouldBe("varchar");
        }

        [Fact]
        public void upsert_argument_defaults()
        {
            theField.UpsertArgument.Arg.ShouldBe("arg_first_name");
            theField.UpsertArgument.Column.ShouldBe("first_name");
            theField.UpsertArgument.PostgresType.ShouldBe("varchar");
        }

        [Fact]
        public void sql_locator_with_default_column_name()
        {
            theField.TypedLocator.ShouldBe("d.first_name");
        }

        [Fact]
        public void sql_locator_with_custom_column_name()
        {
            theField.ColumnName = "x_first_name";
            theField.TypedLocator.ShouldBe("d.x_first_name");
        }

        [Fact]
        public void enum_field()
        {
            var options = new StoreOptions();
            options.Serializer(new JsonNetSerializer
            {
                EnumStorage = EnumStorage.AsString
            });

            var field = DuplicatedField.For<Target>(options, x => x.Color);
            field.UpsertArgument.DbType.ShouldBe(NpgsqlDbType.Varchar);
            field.UpsertArgument.PostgresType.ShouldBe("varchar");

            var constant = Expression.Constant((int)Colors.Blue);

            field.GetValueForCompiledQueryParameter(constant).ShouldBe(Colors.Blue.ToString());
        }


        [Fact]
        public void enum_field_allows_null()
        {
            var options = new StoreOptions();
            options.Serializer(new JsonNetSerializer
            {
                EnumStorage = EnumStorage.AsString
            });

            var field = DuplicatedField.For<Target>(options, x => x.Color);
            field.UpsertArgument.DbType.ShouldBe(NpgsqlDbType.Varchar);
            field.UpsertArgument.PostgresType.ShouldBe("varchar");

            var constant = Expression.Constant(null);

            field.GetValueForCompiledQueryParameter(constant).ShouldBe(null);
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger, "color = CAST(data ->> 'Color' as integer)")]
        [InlineData(EnumStorage.AsString, "color = data ->> 'Color'")]
        public void storage_is_set_when_passed_in(EnumStorage storageMode, string expectedUpdateFragment)
        {
            var storeOptions = new StoreOptions();
            storeOptions.Serializer(new JsonNetSerializer { EnumStorage = storageMode });

            var field = DuplicatedField.For<Target>(storeOptions, x => x.Color);
            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
        }

        [Theory]
        [InlineData(null, "string = data ->> 'String'")]
        [InlineData("varchar", "string = data ->> 'String'")]
        [InlineData("text", "string = data ->> 'String'")]
        public void pg_type_is_used_for_string(string pgType, string expectedUpdateFragment)
        {
            var field = DuplicatedField.For<Target>(new StoreOptions(), x => x.String);
            field.PgType = pgType ?? field.PgType;

            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
            var expectedPgType = pgType ?? "varchar";
            field.PgType.ShouldBe(expectedPgType);
            field.UpsertArgument.PostgresType.ShouldBe(expectedPgType);
            field.DbType.ShouldBe(NpgsqlDbType.Text);
        }

        [Theory]
        [InlineData(null, "user_id = CAST(data ->> 'UserId' as uuid)")]
        [InlineData("uuid", "user_id = CAST(data ->> 'UserId' as uuid)")]
        [InlineData("text", "user_id = CAST(data ->> 'UserId' as text)")]
        public void pg_type_is_used_for_guid(string pgType, string expectedUpdateFragment)
        {
            var field = DuplicatedField.For<Target>(new StoreOptions(), x => x.UserId);
            field.PgType = pgType ?? field.PgType;

            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
            var expectedPgType = pgType ?? "uuid";
            field.PgType.ShouldBe(expectedPgType);
            field.UpsertArgument.PostgresType.ShouldBe(expectedPgType);
            field.DbType.ShouldBe(NpgsqlDbType.Uuid);
        }

        [Theory]
        [InlineData(null, "tags_array = CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(data ->> 'TagsArray' as jsonb))) as varchar[])")]
        [InlineData("varchar[]", "tags_array = CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(data ->> 'TagsArray' as jsonb))) as varchar[])")]
        [InlineData("text[]", "tags_array = CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(data ->> 'TagsArray' as jsonb))) as text[])")]
        public void pg_type_is_used_for_string_array(string pgType, string expectedUpdateFragment)
        {
            var field = DuplicatedField.For<Target>(new StoreOptions(), x => x.TagsArray);
            field.PgType = pgType ?? field.PgType;

            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
            var expectedPgType = pgType ?? "varchar[]";
            field.PgType.ShouldBe(expectedPgType);
            field.UpsertArgument.PostgresType.ShouldBe(expectedPgType);
            field.DbType.ShouldBe(NpgsqlDbType.Array | NpgsqlDbType.Text);
        }

        [Theory]
        [InlineData(null, "tags_list = CAST(data ->> 'TagsList' as jsonb)")]
        [InlineData("varchar[]", "tags_list = CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(data ->> 'TagsList' as jsonb))) as varchar[])")]
        [InlineData("text[]", "tags_list = CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(data ->> 'TagsList' as jsonb))) as text[])")]
        public void pg_type_is_used_for_string_list(string pgType, string expectedUpdateFragment)
        {
            var field = DuplicatedField.For<ListTarget>(new StoreOptions(), x => x.TagsList);
            field.PgType = pgType ?? field.PgType;

            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
            var expectedPgType = pgType ?? "jsonb";
            field.PgType.ShouldBe(expectedPgType);
            field.UpsertArgument.PostgresType.ShouldBe(expectedPgType);
            field.DbType.ShouldBe(NpgsqlDbType.Array | NpgsqlDbType.Text);
        }

        [Theory]
        [InlineData(null, "date = public.mt_immutable_timestamp(data ->> 'Date')")]
        [InlineData("myergen", "date = myergen.mt_immutable_timestamp(data ->> 'Date')")]
        public void store_options_schema_name_is_used_for_timestamp(string schemaName, string expectedUpdateFragment)
        {
            var storeOptions = schemaName != null
                ? new StoreOptions {DatabaseSchemaName = schemaName}
                : new StoreOptions();

            var field = DuplicatedField.For<Target>(storeOptions, x => x.Date);
            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
        }

        [Theory]
        [InlineData(null, "date_offset = public.mt_immutable_timestamptz(data ->> 'DateOffset')")]
        [InlineData("myergen", "date_offset = myergen.mt_immutable_timestamptz(data ->> 'DateOffset')")]
        public void store_options_schema_name_is_used_for_timestamptz(string schemaName, string expectedUpdateFragment)
        {
            var storeOptions = schemaName != null
                ? new StoreOptions {DatabaseSchemaName = schemaName}
                : new StoreOptions();

            var field = DuplicatedField.For<Target>(storeOptions, x => x.DateOffset);
            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
        }

        [Theory]
        [InlineData(Casing.Default, "other_id = CAST(data ->> 'OtherId' as uuid)")]
        [InlineData(Casing.CamelCase, "other_id = CAST(data ->> 'otherId' as uuid)")]
        public void store_options_serializer_with_casing(Casing casing, string expectedUpdateFragment)
        {
            var storeOptions = new StoreOptions();
            storeOptions.UseDefaultSerialization(casing:casing);
            var field = DuplicatedField.For<DuplicateFieldCasingTestDoc>(storeOptions, x => x.OtherId);
            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
        }

        private class ListTarget
        {
            public List<string> TagsList { get; set; }
        }

        private class DuplicateFieldCasingTestDoc
        {
            public Guid Id { get; set; }
            public Guid OtherId { get; set; }
        }
    }
}

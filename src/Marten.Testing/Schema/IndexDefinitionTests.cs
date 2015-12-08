using Marten.Schema;
using Marten.Testing.Fixtures;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class IndexDefinitionTests
    {
        private readonly DocumentMapping mapping = new DocumentMapping(typeof(Target));

        public void default_index_name_with_one_column()
        {
            new IndexDefinition(mapping, "long").IndexName.ShouldBe("mt_doc_target_idx_long");
        }

        public void default_index_name_with_multiple_columns()
        {
            new IndexDefinition(mapping, "foo", "bar").IndexName.ShouldBe("mt_doc_target_idx_foo_bar");
        }

        public void default_method_is_btree()
        {
            new IndexDefinition(mapping, "foo").Method.ShouldBe(IndexMethod.btree);
        }

        public void default_unique_is_false()
        {
            new IndexDefinition(mapping, "foo").IsUnique.ShouldBeFalse();
        }

        public void default_concurrent_is_false()
        {
            new IndexDefinition(mapping, "foo").IsConcurrent.ShouldBeFalse();
        }

        public void default_modifier_is_null()
        {
            new IndexDefinition(mapping, "foo").Modifier.ShouldBeNull();
        }

        public void generate_ddl_for_single_column_all_defaults()
        {
            new IndexDefinition(mapping, "foo").ToDDL()
                .ShouldBe("CREATE INDEX mt_doc_target_idx_foo ON mt_doc_target (foo)");
        }

        public void generate_ddl_for_single_column_unique()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.IsUnique = true;

            definition.ToDDL()
                .ShouldBe("CREATE UNIQUE INDEX mt_doc_target_idx_foo ON mt_doc_target (foo)");
        }

        public void generate_ddl_with_modifier()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.IsUnique = true;
            definition.Modifier = "WITH (fillfactor = 70)";

            definition.ToDDL()
                .ShouldBe("CREATE UNIQUE INDEX mt_doc_target_idx_foo ON mt_doc_target (foo) WITH (fillfactor = 70)");
        }

        public void generate_ddl_for_concurrent_index()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.IsConcurrent = true;

            definition.ToDDL()
                .ShouldBe("CREATE INDEX CONCURRENTLY mt_doc_target_idx_foo ON mt_doc_target (foo)");
        }

        public void generate_ddl_for_concurrent_unique_index()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.IsConcurrent = true;
            definition.IsUnique = true;

            definition.ToDDL()
                .ShouldBe("CREATE UNIQUE INDEX CONCURRENTLY mt_doc_target_idx_foo ON mt_doc_target (foo)");
        }

        public void generate_ddl_for_gin_index()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.Method = IndexMethod.gin;

            definition.ToDDL()
                .ShouldBe("CREATE INDEX mt_doc_target_idx_foo ON mt_doc_target USING gin (foo)");
        }

        public void generate_ddl_for_gin_with_jsonb_path_ops_index()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.Method = IndexMethod.gin;
            definition.Expression = "? jsonb_path_ops";

            definition.ToDDL()
                .ShouldBe("CREATE INDEX mt_doc_target_idx_foo ON mt_doc_target USING gin (foo jsonb_path_ops)");
        }

        public void generate_ddl_for_gist_index()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.Method = IndexMethod.gist;

            definition.ToDDL()
                .ShouldBe("CREATE INDEX mt_doc_target_idx_foo ON mt_doc_target USING gist (foo)");
        }

        public void generate_ddl_for_hash_index()
        {
            var definition = new IndexDefinition(mapping, "foo");
            definition.Method = IndexMethod.hash;

            definition.ToDDL()
                .ShouldBe("CREATE INDEX mt_doc_target_idx_foo ON mt_doc_target USING hash (foo)");
        }
    }
}
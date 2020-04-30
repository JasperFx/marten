using System;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentMapping_schema_patch_writing: DestructiveIntegrationContext
    {
        [Fact]
        public void creates_the_table_in_update_ddl_if_all_new()
        {
            var schemaName = StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            SpecificationExtensions.ShouldContain(patch.UpdateDDL, $"CREATE TABLE {schemaName}.mt_doc_user");
            SpecificationExtensions.ShouldContain(patch.UpdateDDL, $"CREATE OR REPLACE FUNCTION {schemaName}.mt_upsert_user(doc JSONB, docDotNetType varchar, docId uuid, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void drops_the_table_in_rollback_if_all_new()
        {
            var schemaName = StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            SpecificationExtensions.ShouldContain(patch.RollbackDDL, $"drop table if exists {schemaName}.mt_doc_user cascade;");
        }

        [Fact]
        public void drops_the_table_in_rollback_if_all_new_different_schema()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
                _.DatabaseSchemaName = "other";
            });

            var patch = theStore.Schema.ToPatch();

            SpecificationExtensions.ShouldContain(patch.RollbackDDL, "drop table if exists other.mt_doc_user cascade;");
        }

        [Fact]
        public void does_not_drop_the_table_if_it_all_exists()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var patch = theStore.Schema.ToPatch();

            SpecificationExtensions.ShouldNotContain(patch.RollbackDDL, "drop table if exists public.mt_doc_user cascade;");
        }

        [Fact]
        public void can_drop_added_columns_in_document_storage()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.UserName);
            }))
            {
                var patch = store.Schema.ToPatch();

                SpecificationExtensions.ShouldContain(patch.RollbackDDL, "alter table if exists public.mt_doc_user drop column if exists user_name;");
            }
        }

        [Fact]
        public void can_drop_indexes_that_were_added()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Index(x => x.UserName);
            }))
            {
                var patch = store.Schema.ToPatch();

                SpecificationExtensions.ShouldContain(patch.RollbackDDL, "drop index concurrently if exists public.mt_doc_user_idx_user_name;");
            }
        }

        [Fact]
        public void can_revert_indexes_that_changed()
        {
            var schemaName = StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.btree);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = schemaName;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.hash);
            }))
            {
                var patch = store.Schema.ToPatch();

                SpecificationExtensions.ShouldContain(patch.RollbackDDL, "drop index");

                SpecificationExtensions.ShouldContain(patch.RollbackDDL, "CREATE INDEX mt_doc_user_idx_user_name");
            }
        }

        [Fact]
        public void can_revert_indexes_that_changed_in_non_public_schema()
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.btree);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.hash);
            }))
            {
                var patch = store.Schema.ToPatch();

                SpecificationExtensions.ShouldContain(patch.RollbackDDL, "drop index other.mt_doc_user_idx_user_name;");

                SpecificationExtensions.ShouldContain(patch.RollbackDDL, "CREATE INDEX mt_doc_user_idx_user_name ON other.mt_doc_user USING btree (user_name);");
            }
        }

        [Fact]
        public void can_create_and_drop_foreignkeys_todocuments_that_were_added()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES public.mt_doc_user (id)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void for_document_with_int_id_when_schema_patch_applied_then_does_not_show_more_changes()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(IntDoc));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<IntDoc>();
            }))
            {
                Should.NotThrow(() =>
                {
                    store.Schema.ApplyAllConfiguredChangesToDatabase();

                    store.Schema.AssertDatabaseMatchesConfiguration();
                });
            }
        }

        [Fact]
        public void can_create_duplicate_field_with_null_constraint()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Duplicate(x => x.NullableDateTime);
            }))
            {
                var patch = store.Schema.ToPatch();
                SpecificationExtensions.ShouldContain(patch.UpdateDDL, "alter table public.mt_doc_target add column nullable_date_time timestamp without time zone NULL");
            }
        }

        [Fact]
        public void can_create_duplicate_field_with_not_null_constraint()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Duplicate(x => x.Date, notNull: true);
            }))
            {
                var patch = store.Schema.ToPatch();
                SpecificationExtensions.ShouldContain(patch.UpdateDDL, "alter table public.mt_doc_target add column date timestamp without time zone NOT NULL");
            }
        }

        [Fact]
        public void can_create_duplicate_field_with_not_null_constraint_using_duplicate_field_attribute()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<NonNullableDuplicateFieldTest>();
            }))
            {
                var patch = store.Schema.ToPatch();
                SpecificationExtensions.ShouldContain(patch.UpdateDDL, "non_nullable_duplicate_field    timestamp without time zone NOT NULL");
            }
        }

        public DocumentMapping_schema_patch_writing(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class NonNullableDuplicateFieldTest
    {
        public Guid Id { get; set; }
        [DuplicateField(NotNull = true)]
        public DateTime NonNullableDuplicateField { get; set; }
    }
}

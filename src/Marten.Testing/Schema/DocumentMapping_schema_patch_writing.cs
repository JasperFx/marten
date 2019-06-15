using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentMapping_schema_patch_writing: IntegratedFixture
    {
        [Fact]
        public void creates_the_table_in_update_ddl_if_all_new()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("CREATE TABLE public.mt_doc_user");
            patch.UpdateDDL.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user(doc JSONB, docDotNetType varchar, docId uuid, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void drops_the_table_in_rollback_if_all_new()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists public.mt_doc_user cascade;");
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

            patch.RollbackDDL.ShouldContain("drop table if exists other.mt_doc_user cascade;");
        }

        [Fact]
        public void does_not_drop_the_table_if_it_all_exists()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldNotContain("drop table if exists public.mt_doc_user cascade;");
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

                patch.RollbackDDL.ShouldContain("alter table if exists public.mt_doc_user drop column if exists user_name;");
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

                patch.RollbackDDL.ShouldContain("drop index concurrently if exists public.mt_doc_user_idx_user_name;");
            }
        }

        [Fact]
        public void can_revert_indexes_that_changed()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.btree);
            });
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>()
                    .Duplicate(x => x.UserName, configure: i => i.Method = IndexMethod.hash);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.RollbackDDL.ShouldContain("drop index");

                patch.RollbackDDL.ShouldContain("CREATE INDEX mt_doc_user_idx_user_name");
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

                patch.RollbackDDL.ShouldContain("drop index other.mt_doc_user_idx_user_name;");

                patch.RollbackDDL.ShouldContain("CREATE INDEX mt_doc_user_idx_user_name ON other.mt_doc_user USING btree (user_name);");
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
        public void can_update_foreignkeys_todocuments_with_cascade_delete_that_were_added()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES public.mt_doc_user (id);", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);
                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES mt_doc_user(id)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_update_foreignkeys_todocuments_without_cascade_delete_that_were_added()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES public.mt_doc_user (id)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);
                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES mt_doc_user(id);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_create_and_drop_foreignkeys_toexternaltable_that_were_added()
        {
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs (bugid)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_update_foreignkeys_toexternaltable_with_cascade_delete_that_were_added()
        {
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = true);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = false);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs (bugid)", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);
                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs(bugid)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_update_foreignkeys_toexternaltable_without_cascade_delete_that_were_added()
        {
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs (bugid)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);
                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs(bugid);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_add_foreignkeys_toexternaltable_and_delete_that_were_added_asdocument_and_does_not_exist()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs (bugid)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES mt_doc_user(id);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_add_foreignkeys_todocuments_and_delete_that_were_added_asexternaltable_and_does_not_exist()
        {
            theStore.Advanced.Clean.CompletelyRemoveAll();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES public.mt_doc_user (id)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);
                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs(bugid);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_add_foreignkeys_toexternaltable_and_keep_that_were_added_asdocument_and_exists()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldNotContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
                    REFERENCES bugtracker.bugs (bugid)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldNotContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES mt_doc_user(id);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_add_foreignkeys_todocuments_and_keep_that_were_added_asexternaltable_and_exists()
        {
            theStore.Advanced.Clean.CompletelyRemoveAll();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = false);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldNotContain("DROP CONSTRAINT mt_doc_issue_bug_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES public.mt_doc_user (id)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);
                patch.RollbackDDL.ShouldNotContain(@"ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES bugtracker.bugs(bugid);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_replace_foreignkeys_from_todocuments_to_asexternaltable()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.AssigneeId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES bugtracker.bugs (bugid)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES mt_doc_user(id);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        [Fact]
        public void can_replace_foreignkeys_from_asexternaltable_to_todocuments()
        {
            theStore.Advanced.Clean.CompletelyRemoveAll();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            CreateNonMartenTable();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey(i => i.AssigneeId, "bugs", "bugid", "bugtracker", fkd => fkd.CascadeDeletes = false);
            }))
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
            }))
            {
                var patch = store.Schema.ToPatch();

                patch.UpdateDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.UpdateDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES public.mt_doc_user (id)
                    ON DELETE CASCADE;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain("DROP CONSTRAINT mt_doc_issue_assignee_id_fkey;", StringComparisonOption.NormalizeWhitespaces);

                patch.RollbackDDL.ShouldContain(@"ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
                    REFERENCES bugtracker.bugs(bugid);", StringComparisonOption.NormalizeWhitespaces);

                store.Schema.ApplyAllConfiguredChangesToDatabase();
            }
        }

        private void CreateNonMartenTable()
        {
            using (var sesion = theStore.OpenSession())
            {
                sesion.Connection.RunSql(@"CREATE SCHEMA IF NOT EXISTS bugtracker;");
                sesion.Connection.RunSql(
                @"CREATE TABLE IF NOT EXISTS bugtracker.bugs (
                    bugid			    uuid CONSTRAINT pk_mt_streams PRIMARY KEY,
                    name                varchar(100) NULL
                )");
            }
        }
    }
}
using Marten.Testing.Documents;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentMapping_schema_patch_foreign_key_writing: IntegratedFixture
    {
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
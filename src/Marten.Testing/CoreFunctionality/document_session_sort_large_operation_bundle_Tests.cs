using System;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    using System.Linq.Expressions;

    public sealed class document_session_sort_large_operation_bundle_Tests: IntegrationContext
    {
        public document_session_sort_large_operation_bundle_Tests(DefaultStoreFixture fixture): base(fixture)
        {
        }

        [Fact]
        public void save_large_bundle_of_operations()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.DeleteWhere<TestModel5>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);

                session.Patch<TestModel5>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage == 1)
                    .Set(x => x.Name, "Matrix 1");

                session.DeleteWhere<TestModel6>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);

                session.Patch<TestModel6>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage == 1)
                    .Set(x => x.Text, "");

                Expression<Func<TestModel4, bool>> definitionFilter = x => x.ObjectId == id1 && x.DefinitionId == 1;
                session.Patch(definitionFilter).Set(x => x.Stages, 1);
                session.Patch(definitionFilter).Set(x => x.Bool5, false);
                session.Patch(definitionFilter).Set(x => x.Text, "");
                session.Patch(definitionFilter).Set(x => x.Bool4, false);
                session.Patch(definitionFilter).Set(x => x.Mode, "Automatic");
                session.Patch(definitionFilter).Set(x => x.Bool3, true);
                session.Patch(definitionFilter).Set(x => x.Bool1, true);
                session.Patch(definitionFilter).Set(x => x.Bool2, true);
                session.Patch(definitionFilter).Set(x => x.Fields1, "||");
                session.Patch(definitionFilter).Set(x => x.Fields2, "||");
                session.Patch(definitionFilter).Set(x => x.Attr, "|gwk-id|pt-id|");

                session.DeleteWhere<TestModel3>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);
                session.Patch<TestModel3>(x => x.ObjectId == id1 && x.DefinitionId == 1)
                    .Set(x => x.Cond1, 1);
                session.Patch<TestModel3>(x => x.ObjectId == id1 && x.DefinitionId == 1)
                    .Set(x => x.Cond2, 1);

                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test2");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test3");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test4");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test5");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test6");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test7");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test8");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test9");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test10");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test11");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test12");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test13");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test14");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test15");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test16");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test17");

                session.DeleteWhere<TestModel1>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);
                session.Patch<TestModel1>(x => x.ObjectId == id1 && x.DefinitionId == 1)
                    .Set(x => x.Mode, 1);

                session.SaveChanges();
            }
        }

        public class TestModel1
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public int Stage { get; set; }
            public int Mode { get; set; }
        }

        public class TestModel2
        {
            public Guid Id { get; set; }
            public string Content { get; set; }
        }

        public class TestModel3
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public int Stage { get; set; }
            public int Cond1 { get; set; }
            public int Cond2 { get; set; }
        }


        public class TestModel4
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public bool Bool1 { get; set; }
            public bool Bool2 { get; set; }
            public bool Bool3 { get; set; }
            public string Fields1 { get; set; }
            public string Fields2 { get; set; }
            public string Attr { get; set; }
            public bool Bool5 { get; set; }
            public int Stages { get; set; }
            public string Text { get; set; }
            public bool Bool4 { get; set; }
            public string Mode { get; set; }
        }

        public class TestModel5
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public string Name { get; set; }
            public int Stage { get; set; }
        }

        public class TestModel6
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public int Stage { get; set; }
            public string Text { get; set; }
        }
    }
}

using System;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Testing.CoreFunctionality;
using Marten.Testing.Documents;
using Marten.V4Internals;
using Shouldly;
using Xunit;

namespace Marten.Testing.V4Internals.CodeGeneration
{
    public class UpsertOperationBuilderTests
    {
        private readonly DocumentMapping theMapping = DocumentMapping.For<User>();

        private GeneratedType _type;
        private UpsertOperationBuilder _builder;
        private GeneratedAssembly theGeneratedAssembly;

        private IStorageOperation theOperation
        {
            get
            {
                theGeneratedType.ShouldNotBeNull();

                var compiler = new LamarCompiler.AssemblyGenerator();
                compiler.ReferenceAssembly(typeof(DocumentStorageBuilder).Assembly);
                compiler.ReferenceAssembly(typeof(User).Assembly);

                compiler.Compile(theGeneratedAssembly);

                var realType = theGeneratedType.CompiledType;

                var user = new User();
                return (IStorageOperation) Activator.CreateInstance(realType, user, Guid.NewGuid());
            }
        }

        protected UpsertOperationBuilder theBuilder
        {
            get
            {
                if (_builder == null)
                {
                    _builder = new UpsertOperationBuilder(theMapping);
                }

                return _builder;
            }
        }

        protected GeneratedType theGeneratedType
        {
            get
            {
                if (_type == null)
                {
                    theGeneratedAssembly = new GeneratedAssembly(new GenerationRules());
                    _type = theBuilder.BuildType(theGeneratedAssembly);
                }

                return _type;
            }
        }

        /*
         * TODO
         * With optimistic locking
         * With multi-tenancy
         */

        [Fact]
        public void class_name_basic()
        {
            theBuilder
                .ClassName.ShouldBe("UpsertUserOperation");
        }

        [Fact]
        public void build_command_text_basic()
        {
            theBuilder
                .CommandText.ShouldBe("select public.mt_upsert_user(?, ?, ?, ?)");
        }

        [Fact]
        public void should_implement_istorageoperation()
        {
            theGeneratedType.Interfaces.ShouldContain(typeof(IStorageOperation));
        }

        [Fact]
        public void should_add_a_constant_for_the_command_text()
        {
            theGeneratedType.Setters.Any(x => x.Type == SetterType.Constant && x.PropName == "CommandText")
                .ShouldBeTrue();
        }

        [Fact]
        public void role_should_be_upsert()
        {
            theOperation.Role.ShouldBe(StorageRole.Upsert);
        }

        [Fact]
        public void the_document_type()
        {
            theOperation.DocumentType.ShouldBe(typeof(User));
        }


    }
}

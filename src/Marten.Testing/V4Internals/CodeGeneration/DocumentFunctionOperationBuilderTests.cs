using System;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.CoreFunctionality;
using Marten.Testing.Documents;
using Marten.V4Internals;
using Shouldly;
using Xunit;

namespace Marten.Testing.V4Internals.CodeGeneration
{
    public class DocumentFunctionOperationBuilderTests
    {
        private readonly DocumentMapping theMapping = DocumentMapping.For<User>();

        private GeneratedType _type;
        private DocumentFunctionOperationBuilder _builder;
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
                return (IStorageOperation) Activator.CreateInstance(realType, user, Guid.NewGuid(), Guid.NewGuid());
            }
        }

        protected DocumentFunctionOperationBuilder theBuilder
        {
            get
            {
                if (_builder == null)
                {
                    _builder = new DocumentFunctionOperationBuilder(theMapping, new UpsertFunction(theMapping), StorageRole.Upsert);
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
            theOperation.GetType().CanBeCastTo<IStorageOperation>()
                .ShouldBeTrue();
        }

        [Fact]
        public void role_should_be_upsert()
        {
            theOperation.Role().ShouldBe(StorageRole.Upsert);
        }

        [Fact]
        public void the_document_type()
        {
            theOperation.DocumentType.ShouldBe(typeof(User));
        }


    }
}

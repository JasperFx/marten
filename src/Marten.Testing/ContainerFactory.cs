using StructureMap;

namespace Marten.Testing
{
    public static class ContainerFactory
    {
        public static IContainer Default()
        {
            return Container.For<DevelopmentModeRegistry>();
        }

        public static IContainer OnOtherDatabaseSchema()
        {
            var registry = new DevelopmentModeRegistry(options => options.DatabaseSchemaName = "other");
            return new Container(registry);
        }
    }
}
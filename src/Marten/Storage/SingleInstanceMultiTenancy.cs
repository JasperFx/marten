namespace Marten.Storage
{
    /*
     * Other things
     * 1. Use a default, template database that you can use to seed everything
     * 
     */

    public class SingleInstanceMultiTenancy : ITenantStrategy
    {
        public string[] AllKnownTenants()
        {
            throw new System.NotImplementedException();
        }

        public IConnectionFactory Create(string tenantId, AutoCreate autoCreate)
        {
            throw new System.NotImplementedException();
        }


    }
}
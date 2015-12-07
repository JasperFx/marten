using System;

namespace Marten.Testing.Examples
{
    // SAMPLE: id_samples
    public class User
    {
        // String property as Id
        public string Id { get; set; }
    }

    public class Category
    {
        // Guid's work, fields too
        public Guid Id;
    }

    public class Company
    {
        // int's and long's can be the Id
        // "id" is accepted
        public int id { get; set; }
    }
    // ENDSAMPLE
}
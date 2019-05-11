using System;
using System.Collections.Generic;

namespace Marten.Testing.Linq.ChildCollections
{
    public class Root
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public ICollection<ChildLevel1> ChildsLevel1 { get; set; }
    }

    public class ChildLevel1
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public ICollection<ChildLevel2> ChildsLevel2 { get; set; }
    }

    public class ChildLevel2
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
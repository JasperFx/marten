using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs
{
    public class Bug_1871_includes_with_snake_case_json : BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1871_includes_with_snake_case_json(ITestOutputHelper output)
        {
            _output = output;
        }

        public class Role
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        public class RateCard
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public List<RateCardRole> Roles { get; set; } = new();
            public List<Guid> RoleIds => Roles.Select(x => x.RoleId).ToList();
        }

        public class RateCardRole
        {
            public Guid Id { get; set; }
            public Guid RoleId { get; set; }
            public decimal Rate { get; set; }
        }

        [Fact]
        public async Task includes_should_work()
        {
            StoreOptions(opts =>
            {
                opts.UseDefaultSerialization(casing: Casing.SnakeCase);
                opts.Logger(new TestOutputMartenLogger(_output));
            });

            var admin = new Role {Name = "Admin"};
            var superuser = new Role {Name = "Super"};
            var auditor = new Role {Name = "Auditor"};

            theSession.Store(admin, superuser, auditor);
            await theSession.SaveChangesAsync();

            var card1 = new RateCard
            {
                Roles = new List<RateCardRole>{new RateCardRole{RoleId = admin.Id}, new RateCardRole{RoleId = superuser.Id}, new RateCardRole{RoleId = auditor.Id}}
            };
            theSession.Store(card1);
            await theSession.SaveChangesAsync();

            var session = theSession;

            var roles = new Dictionary<Guid, Role>();
            var cards = await session
                .Query<RateCard>()
                .Include(x => x.RoleIds, roles)
                .ToListAsync();

            roles[admin.Id].Id.ShouldBe(admin.Id);
            roles[superuser.Id].Id.ShouldBe(superuser.Id);
            roles[auditor.Id].Id.ShouldBe(auditor.Id);
        }
    }
}

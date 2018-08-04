using System;
using Marten.Events;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Util;
using Shouldly;
using Xunit;
using System.Collections.Generic;
using System.Linq;


namespace Marten.Testing.Bugs
{
	public class Bug_1060_invalid_cast_exception_on_doc_with_subclass : IntegratedFixture
	{
		[Fact]
		public void can_issue_query_on_doc_hierarchy_with_include()
		{
            StoreOptions(_ => {
                _.Schema.For<User>()
                                 .AddSubClass<SuperUser>()
                                 .AddSubClass<AdminUser>(); 
                _.Schema.For<Issue>(); 
            });

            var user = new User { Id = System.Guid.NewGuid() }; 
            var admin = new AdminUser { Id = System.Guid.NewGuid() };
            var issue = new Issue { Id = System.Guid.NewGuid(), ReporterId = user.Id };
            var issue2 = new Issue { Id = System.Guid.NewGuid(), ReporterId = admin.Id };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.Store(admin);
                session.Store(issue);
                session.Store(issue2);
                session.SaveChanges();

                var users = new List<User>();
                var admins = new List<AdminUser>();

                var userIssues = session.Query<Issue>()
                                        .Include(i => i.ReporterId, users)
                                        .ToList();

                var adminIssues = session.Query<Issue>()
                                         .Include(i => i.ReporterId, admins)
                                         .ToList();

                // validate for parent document (base class)
                users.Count(p => p!=null).ShouldBe(2);

                // validate for subclass document
                admins.Count(p => p!=null).ShouldBe(1);
            }
		}
	}
}
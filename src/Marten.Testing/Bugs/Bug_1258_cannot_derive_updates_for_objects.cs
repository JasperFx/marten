using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services.Includes;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
public class Bug_1258_cannot_derive_updates_for_objects : IntegratedFixture
    {
        [Fact]
        public void can_properly_detect_changes_when_user_defined_type()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Schema.For<UserWithCustomType>();
                _.Schema.For<IssueForUserWithCustomType>().ForeignKey<UserWithCustomType>(x => x.UserId);
                _.Schema.For<IssueForUserWithCustomType>().GinIndexJsonData();
                _.Schema.For<UserWithCustomType>().Duplicate(u => u.Name, pgType: "cust_type", configure: idx =>
                {
                    idx.IsUnique = true;
                });
                _.Schema.For<UserWithCustomType>().GinIndexJsonData();
            });

            var guyWithCustomType1 = new UserWithCustomType { Id = Guid.NewGuid(), Name = "test_guy", CustomType = "test_cust_type" };
            var guyWithCustomType2 = new UserWithCustomType { Id = Guid.NewGuid(), Name = "another_test_guy", CustomType = "test_cust_type" };

            var issue1 = new IssueForUserWithCustomType { UserId = guyWithCustomType1.Id, Title = "Issue #1" };
            var issue2 = new IssueForUserWithCustomType { UserId = guyWithCustomType2.Id, Title = "Issue #2" };
            var issue3 = new IssueForUserWithCustomType { UserId = guyWithCustomType2.Id, Title = "Issue #3" };

            using (var session = theStore.OpenSession())
            {
                var cmd = new NpgsqlCommand(@"
                    DROP OPERATOR CLASS IF EXISTS cust_type_ops USING btree;
                    DROP OPERATOR IF EXISTS ~=~ (cust_type, cust_type);
                    DROP OPERATOR IF EXISTS ~<>~ (cust_type, cust_type);
                    DROP OPERATOR IF EXISTS ~<=~ (cust_type, cust_type);
                    DROP OPERATOR IF EXISTS ~<~ (cust_type, cust_type);
                    DROP OPERATOR IF EXISTS ~>~ (cust_type, cust_type);
                    DROP OPERATOR IF EXISTS ~>=~ (cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_eq(cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_lt(cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_le(cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_ge(cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_gt(cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_ne(cust_type, cust_type);
                    DROP FUNCTION IF EXISTS cust_type_compare(cust_type, cust_type);
                    DROP TYPE IF EXISTS cust_type CASCADE;
                    DROP FUNCTION IF EXISTS cust_type_in(cstring);
                    DROP FUNCTION IF EXISTS cust_type_out(cust_type);

                    CREATE TYPE cust_type;

                    CREATE FUNCTION cust_type_in(cstring)
                       RETURNS cust_type
                       AS 'textin'
                       LANGUAGE internal STRICT IMMUTABLE;
                    CREATE FUNCTION cust_type_out(cust_type)
                       RETURNS cstring
                       AS 'textout'
                       LANGUAGE internal STRICT IMMUTABLE;

                    CREATE TYPE cust_type (
                       internallength = variable,
                       input = cust_type_in,
                       output = cust_type_out
                    );

                    CREATE OR REPLACE FUNCTION strcmp(text, text) RETURNS int AS $$
                        SELECT CASE WHEN $1 < $2 THEN -1
                        WHEN $1 > $2 THEN 1
                        ELSE 0 END;
                    $$ LANGUAGE sql;

                    CREATE FUNCTION cust_type_compare(cust_type, cust_type) RETURNS integer AS $$
                    BEGIN
                        RETURN strcmp($1::text, $2::text);
                    END;
                    $$ LANGUAGE plpgsql;

                    CREATE FUNCTION cust_type_eq(cust_type, cust_type) RETURNS boolean IMMUTABLE LANGUAGE sql
                       AS 'SELECT cust_type_compare($1, $2) = 0';
                    CREATE FUNCTION cust_type_lt(cust_type, cust_type) RETURNS boolean IMMUTABLE LANGUAGE sql
                       AS 'SELECT cust_type_compare($1, $2) = -1';
                    CREATE FUNCTION cust_type_le(cust_type, cust_type) RETURNS boolean IMMUTABLE LANGUAGE sql
                       AS 'SELECT cust_type_compare($1, $2) <= 0';
                    CREATE FUNCTION cust_type_ge(cust_type, cust_type) RETURNS boolean IMMUTABLE LANGUAGE sql
                       AS 'SELECT cust_type_compare($1, $2) >= 0';
                    CREATE FUNCTION cust_type_gt(cust_type, cust_type) RETURNS boolean IMMUTABLE LANGUAGE sql
                       AS 'SELECT cust_type_compare($1, $2) = 1';
                    CREATE FUNCTION cust_type_ne(cust_type, cust_type) RETURNS boolean IMMUTABLE LANGUAGE sql
                       AS 'SELECT cust_type_compare($1, $2) <> 0';

                    CREATE OPERATOR ~=~ (
                       PROCEDURE = cust_type_eq,
                       LEFTARG = cust_type,
                       RIGHTARG = cust_type,
                       COMMUTATOR = ~=~,
                       NEGATOR = ~<>~
                    );
                    CREATE OPERATOR ~<>~ (
                       PROCEDURE = cust_type_ne,
                       LEFTARG = cust_type,
                       RIGHTARG = cust_type,
                       COMMUTATOR = ~<>~,
                       NEGATOR = ~=~
                    );
                    CREATE OPERATOR ~<=~ (
                       PROCEDURE = cust_type_le,
                       LEFTARG = cust_type,
                       RIGHTARG = cust_type,
                       COMMUTATOR = ~>=~,
                       NEGATOR = ~>~
                    ); 
                    CREATE OPERATOR ~<~ (
                       PROCEDURE = cust_type_lt,
                       LEFTARG = cust_type,
                       RIGHTARG = cust_type,
                       COMMUTATOR = ~>~,
                       NEGATOR = ~>=~
                    );
                    CREATE OPERATOR ~>~ (
                       PROCEDURE = cust_type_gt,
                       LEFTARG = cust_type,
                       RIGHTARG = cust_type,
                       COMMUTATOR = ~<~,
                       NEGATOR = ~<=~
                    );
                    CREATE OPERATOR ~>=~ (
                       PROCEDURE = cust_type_ge,
                       LEFTARG = cust_type,
                       RIGHTARG = cust_type,
                       COMMUTATOR = ~<=~,
                       NEGATOR = ~<~
                    );

                    CREATE OPERATOR CLASS cust_type_ops
                    DEFAULT FOR TYPE cust_type USING btree AS
                       OPERATOR 1 ~<~(cust_type,cust_type),
                       OPERATOR 2 ~<=~(cust_type,cust_type),
                       OPERATOR 3 ~=~(cust_type,cust_type),
                       OPERATOR 4 ~>=~(cust_type,cust_type),
                       OPERATOR 5 ~>~(cust_type,cust_type),
                       FUNCTION 1 cust_type_compare(cust_type,cust_type);", session.Connection);
                cmd.ExecuteNonQuery();
            }

            using (var session = theStore.LightweightSession())
            {
                session.Store(guyWithCustomType1, guyWithCustomType2);
                session.Store(issue1, issue2, issue3);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Load<UserWithCustomType>(guyWithCustomType1.Id).CustomType.ShouldBe("test_cust_type");
            }

            using (var query = theStore.QuerySession())
            {
                var userList = new List<UserWithCustomType>();

                var issues = query.Query<IssueForUserWithCustomType>().Include<UserWithCustomType>(x => x.UserId, userList, JoinType.LeftOuter).ToArray();

                userList.Count.ShouldBe(2);

                userList.Any(x => x.Id == guyWithCustomType1.Id);
                userList.Any(x => x.Id == guyWithCustomType2.Id);
                userList.Any(x => x == null);

                issues.Length.ShouldBe(3);
            }

            var secondStore = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Schema.For<UserWithCustomType>();
                _.Schema.For<IssueForUserWithCustomType>().ForeignKey<UserWithCustomType>(x => x.UserId);
                _.Schema.For<IssueForUserWithCustomType>().GinIndexJsonData();
                _.Schema.For<UserWithCustomType>().Duplicate(u => u.Name, pgType: "cust_type", configure: idx =>
                {
                    idx.IsUnique = true;
                });
                _.Schema.For<UserWithCustomType>().GinIndexJsonData();
            });

            using (var query = secondStore.QuerySession())
            {
                var userList = new List<UserWithCustomType>();

                var issues = query.Query<IssueForUserWithCustomType>().Include<UserWithCustomType>(x => x.UserId, userList, JoinType.LeftOuter).ToArray();

                userList.Count.ShouldBe(2);

                userList.Any(x => x.Id == guyWithCustomType1.Id);
                userList.Any(x => x.Id == guyWithCustomType2.Id);
                userList.Any(x => x == null);

                issues.Length.ShouldBe(3);
            }
        }
    }

    public class UserWithCustomType
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string CustomType { get; set; }
    }

    public class IssueForUserWithCustomType
    {
        public Guid Id { get; set; }

        public string Title { get; set; }

        public Guid? UserId { get; set; }
    }
}

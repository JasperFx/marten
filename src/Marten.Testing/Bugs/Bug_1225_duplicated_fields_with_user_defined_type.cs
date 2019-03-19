using System;
using Marten.Schema;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1225_duplicated_fields_with_user_defined_type : IntegratedFixture
    {
        [Fact]
        public void can_insert_new_docs()
        {
            var guyWithCustomType = new GuyWithCustomType { CustomType = "test" };

            using (var session = theStore.OpenSession())
            {
                var cmd = new NpgsqlCommand(@"CREATE TYPE cust_type;
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
                session.Store(guyWithCustomType);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Load<GuyWithCustomType>(guyWithCustomType.Id).CustomType.ShouldBe("test");
            }
        }
    }

    public class GuyWithCustomType
    {
        public Guid Id { get; set; }

        [DuplicateField(PgType = "cust_type")]
        public string CustomType { get; set; }
    }
}

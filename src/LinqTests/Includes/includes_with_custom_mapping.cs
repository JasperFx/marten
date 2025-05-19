#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Includes;

public class includes_with_custom_mapping : IntegrationContext
{
    private readonly ITestOutputHelper _testOutputHelper;
    private const string TenantId = "the_tenant_id";

    public includes_with_custom_mapping(DefaultStoreFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        _testOutputHelper = testOutputHelper;
    }

    protected override async Task fixtureSetup()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        var classrooms = Enumerable.Range(0, 5)
            .Select(i => new Classroom(CombGuidIdGeneration.NewGuid(), $"AA-{i}", 10 + i)).ToList();
        var teachers = Enumerable.Range(10, 10)
            .Select(i => new SchoolUser(CombGuidIdGeneration.NewGuid(), $"Teacher-{i}", null, i)).ToList();
        var students = Enumerable.Range(1000, 100)
            .Select(i => new SchoolUser(CombGuidIdGeneration.NewGuid(), $"Student-{i}", $"AA-{i % 4}")).ToList();

        var classrooms2 = classrooms.Select(c => new Classroom2(c.Id, c.RoomCode, c.TeacherId));
        var teachers2 = teachers.Select(t => new SchoolUser2(t.Id, t.Name, t.HomeRoom, t.StaffId));
        var students2 = students.Select(s => new SchoolUser2(s.Id, s.Name, s.HomeRoom, s.StaffId));

        await theStore.BulkInsertDocumentsAsync(
            TenantId,
            [
                ..classrooms, ..teachers, ..students, ..classrooms2, ..teachers2, ..students2
            ]);
    }

    #region data-path

    [Fact]
    public async Task include_for_a_single_mapped_document_int_one_to_one()
    {
        SchoolUser? teacher = null;

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include<SchoolUser>(u => teacher = u).On(r => r.TeacherId, u => u.StaffId)
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classRoom = await query.SingleAsync();

        teacher.ShouldNotBeNull();
        teacher.StaffId.ShouldBe(11);
        teacher.Name.ShouldBe("Teacher-11");

        classRoom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_string_one_to_one()
    {
        Classroom? homeRoom = null;

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser>()
            .Include<Classroom>(c => homeRoom = c).On(u => u.HomeRoom, c => c.RoomCode)
            .Where(u => u.Name == "Student-1002");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        homeRoom.ShouldNotBeNull();
        homeRoom.RoomCode.ShouldBe("AA-2");

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_one_to_many_list()
    {
        var students = new List<SchoolUser>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom)
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        students.ShouldAllBe(s => s.HomeRoom == "AA-1");
        students.Count.ShouldBe(25);
        students.ShouldBeUnique();

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_list()
    {
        var students = new List<SchoolUser>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom);

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        students.Count.ShouldBe(100);
        students.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_many_to_one_list()
    {
        var homeRooms = new List<Classroom>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(4);
        students.ShouldBeUnique();

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_string_many_to_one_dictionary()
    {
        var homeRooms = new Dictionary<string, Classroom>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(4);
        homeRooms.ShouldAllBe(kvp => kvp.Key == kvp.Value.RoomCode);

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_int_many_to_one_dictionary()
    {
        var teachers = new Dictionary<int, SchoolUser>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(teachers).On(r => r.TeacherId, u => u.StaffId);

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        teachers.Count.ShouldBe(5);
        teachers.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_dictionary()
    {
        var students = new Dictionary<string, IList<SchoolUser>>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom);

        LogCommand(query);

        var classRoom = await query.ToListAsync();

        students.Count.ShouldBe(4);
        students.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        students.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        students.SelectMany(kvp => kvp.Value).Count().ShouldBe(100);

        classRoom.ShouldNotBeNull();
        classRoom.Count.ShouldBe(5);
    }

    #endregion

    #region data-path with filtering

    [Fact]
    public async Task include_for_a_single_mapped_document_int_one_to_one_filtered()
    {
        SchoolUser? teacher = null;

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include<SchoolUser>(u => teacher = u).On(r => r.TeacherId, u => u.StaffId, u => u.Name == "Invalid")
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classRoom = await query.SingleAsync();

        teacher.ShouldBeNull();
        classRoom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_string_one_to_one_filtered()
    {
        Classroom? homeRoom = null;

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser>()
            .Include<Classroom>(c => homeRoom = c)
            .On(u => u.HomeRoom, c => c.RoomCode, c => c.RoomCode == "Invalid")
            .Where(u => u.Name == "Student-1002");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        homeRoom.ShouldBeNull();
        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_one_to_many_list_filtered()
    {
        var students = new List<SchoolUser>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"))
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        students.ShouldAllBe(s => s.HomeRoom == "AA-1");
        students.Count.ShouldBe(5);
        students.ShouldBeUnique();

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_list_filtered()
    {
        var students = new List<SchoolUser>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"));

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        students.Count.ShouldBe(10);
        students.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_many_to_one_list_filtered()
    {
        var homeRooms = new List<Classroom>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode, r => r.TeacherId < 13)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(3);
        students.ShouldBeUnique();

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_string_many_to_one_dictionary_filtered()
    {
        var homeRooms = new Dictionary<string, Classroom>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode, r => r.TeacherId < 13)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(3);
        homeRooms.ShouldAllBe(kvp => kvp.Key == kvp.Value.RoomCode);

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_int_many_to_one_dictionary_filtered()
    {
        var teachers = new Dictionary<int, SchoolUser>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(teachers).On(r => r.TeacherId, u => u.StaffId, u => u.StaffId < 13);

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        teachers.Count.ShouldBe(3);
        teachers.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_dictionary_filtered()
    {
        var students = new Dictionary<string, IList<SchoolUser>>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"));

        LogCommand(query);

        var classRoom = await query.ToListAsync();

        students.Count.ShouldBe(2);
        students.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        students.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        students.SelectMany(kvp => kvp.Value).Count().ShouldBe(10);

        classRoom.ShouldNotBeNull();
        classRoom.Count.ShouldBe(5);
    }

    #endregion

    #region duplicate-fields

    [Fact]
    public async Task include_for_a_single_mapped_document_int_one_to_one_duplicate_fields()
    {
        SchoolUser2? teacher = null;

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include<SchoolUser2>(u => teacher = u).On(r => r.TeacherId, u => u.StaffId)
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classRoom = await query.SingleAsync();

        teacher.ShouldNotBeNull();
        teacher.StaffId.ShouldBe(11);
        teacher.Name.ShouldBe("Teacher-11");

        classRoom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_string_one_to_one_duplicate_fields()
    {
        Classroom2? homeRoom = null;

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser2>()
            .Include<Classroom2>(c => homeRoom = c).On(u => u.HomeRoom, c => c.RoomCode)
            .Where(u => u.Name == "Student-1002");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        homeRoom.ShouldNotBeNull();
        homeRoom.RoomCode.ShouldBe("AA-2");

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_one_to_many_list_duplicate_fields()
    {
        var students = new List<SchoolUser2>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom)
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        students.ShouldAllBe(s => s.HomeRoom == "AA-1");
        students.Count.ShouldBe(25);
        students.ShouldBeUnique();

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_list_duplicate_fields()
    {
        var students = new List<SchoolUser2>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom);

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        students.Count.ShouldBe(100);
        students.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_many_to_one_list_duplicate_fields()
    {
        var homeRooms = new List<Classroom2>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser2>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(4);
        students.ShouldBeUnique();

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_string_many_to_one_dictionary_duplicate_fields()
    {
        var homeRooms = new Dictionary<string, Classroom2>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser2>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(4);
        homeRooms.ShouldAllBe(kvp => kvp.Key == kvp.Value.RoomCode);

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_int_many_to_one_dictionary_duplicate_fields()
    {
        var teachers = new Dictionary<int, SchoolUser2>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(teachers).On(r => r.TeacherId, u => u.StaffId);

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        teachers.Count.ShouldBe(5);
        teachers.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_dictionary_duplicate_fields()
    {
        var students = new Dictionary<string, IList<SchoolUser2>>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom);

        LogCommand(query);

        var classRoom = await query.ToListAsync();

        students.Count.ShouldBe(4);
        students.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        students.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        students.SelectMany(kvp => kvp.Value).Count().ShouldBe(100);

        classRoom.ShouldNotBeNull();
        classRoom.Count.ShouldBe(5);
    }

    #endregion

    #region duplicate-fields with filtering

    [Fact]
    public async Task include_for_a_single_mapped_document_int_one_to_one_filtered_duplicate_fields()
    {
        SchoolUser2? teacher = null;

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include<SchoolUser2>(u => teacher = u).On(r => r.TeacherId, u => u.StaffId, u => u.Name == "Invalid")
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classRoom = await query.SingleAsync();

        teacher.ShouldBeNull();
        classRoom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_string_one_to_one_filtered_duplicate_fields()
    {
        Classroom2? homeRoom = null;

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser2>()
            .Include<Classroom2>(c => homeRoom = c)
            .On(u => u.HomeRoom, c => c.RoomCode, c => c.RoomCode == "Invalid")
            .Where(u => u.Name == "Student-1002");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        homeRoom.ShouldBeNull();
        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_one_to_many_list_filtered_duplicate_fields()
    {
        var students = new List<SchoolUser2>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"))
            .Where(r => r.RoomCode == "AA-1");

        LogCommand(query, FetchType.FetchOne);

        var classroom = await query.SingleAsync();

        students.ShouldAllBe(s => s.HomeRoom == "AA-1");
        students.Count.ShouldBe(5);
        students.ShouldBeUnique();

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_list_filtered_duplicate_fields()
    {
        var students = new List<SchoolUser2>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"));

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        students.Count.ShouldBe(10);
        students.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_many_to_one_list_filtered_duplicate_fields()
    {
        var homeRooms = new List<Classroom2>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser2>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode, r => r.TeacherId < 13)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(3);
        students.ShouldBeUnique();

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_string_many_to_one_dictionary_filtered_duplicate_fields()
    {
        var homeRooms = new Dictionary<string, Classroom2>();

        var query = theStore.QuerySession(TenantId)
            .Query<SchoolUser2>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode, r => r.TeacherId < 13)
            .Where(s => s.StaffId == null);

        LogCommand(query);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(3);
        homeRooms.ShouldAllBe(kvp => kvp.Key == kvp.Value.RoomCode);

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_int_many_to_one_dictionary_filtered_duplicate_fields()
    {
        var teachers = new Dictionary<int, SchoolUser2>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(teachers).On(r => r.TeacherId, u => u.StaffId, u => u.StaffId < 13);

        LogCommand(query);

        var classrooms = await query.ToListAsync();

        teachers.Count.ShouldBe(3);
        teachers.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_dictionary_filtered_duplicate_fields()
    {
        var students = new Dictionary<string, IList<SchoolUser2>>();

        var query = theStore.QuerySession(TenantId)
            .Query<Classroom2>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"));

        LogCommand(query);

        var classRoom = await query.ToListAsync();

        students.Count.ShouldBe(2);
        students.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        students.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        students.SelectMany(kvp => kvp.Value).Count().ShouldBe(10);

        classRoom.ShouldNotBeNull();
        classRoom.Count.ShouldBe(5);
    }

    #endregion

    [Fact]
    public async Task batch_query_mapped_includes()
    {
        await using var session = theStore.QuerySession(TenantId);
        var batch = session.CreateBatchQuery();

        SchoolUser? include1 = null;
        var query1 = batch
            .Query<Classroom>()
            .Include<SchoolUser>(u => include1 = u).On(r => r.TeacherId, u => u.StaffId)
            .Where(r => r.RoomCode == "AA-1").Single();

        var include2 = new List<SchoolUser>();

        var query2 = batch
            .Query<Classroom>()
            .Include(include2).On(r => r.RoomCode, u => u.HomeRoom)
            .ToList();

        var include3 = new Dictionary<string, IList<SchoolUser2>>();
        var query3 = batch
            .Query<Classroom2>()
            .Include(include3).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"))
            .ToList();


        await batch.Execute();


        include1.ShouldNotBeNull().StaffId.ShouldBe(11);
        (await query1).ShouldNotBeNull();

        include2.Count.ShouldBe(100);
        include2.ShouldBeUnique();
        (await query2).ShouldNotBeNull().Count.ShouldBe(5);

        include3.Count.ShouldBe(2);
        include3.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        include3.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        include3.SelectMany(kvp => kvp.Value).Count().ShouldBe(10);
        (await query3).ShouldNotBeNull().Count.ShouldBe(5);
    }

    /// <summary>
    /// Easy preview of the sql query
    /// </summary>
    private void LogCommand<T>(IQueryable<T> queryable, FetchType fetchType = FetchType.FetchMany)
    {
        var command = queryable.ToCommand(fetchType);

        _testOutputHelper.WriteLine(
            "{0}\nParameters:\n{1}",
            command.CommandText.Replace(";", "; \n"),
            command.Parameters.Select(p => $"  {p.ParameterName}: {p.NpgsqlValue}").Join("\n"));
    }

    #region Test Models

    // alias is to avoid long name issues
    [DocumentAlias("school_user")]
    public record SchoolUser(Guid Id, string Name, string? HomeRoom, int? StaffId = null);

    [DocumentAlias("classroom")]
    public record Classroom(Guid Id, string RoomCode, int? TeacherId = null);

    // duplicate field variants
    [DocumentAlias("school_user_dup_field")]
    public record SchoolUser2(
        Guid Id,
        string Name,
        [property: DuplicateField] string? HomeRoom,
        [property: DuplicateField] int? StaffId = null);

    [DocumentAlias("classroom_dup_field")]
    public record Classroom2(
        Guid Id,
        [property: DuplicateField] string RoomCode,
        [property: DuplicateField] int? TeacherId = null);

    #endregion
}

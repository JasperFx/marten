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

    public includes_with_custom_mapping(DefaultStoreFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_int_one_to_one()
    {
        await InsertSeedData();

        SchoolUser? teacher = null;

        var query = theSession
            .Query<Classroom>()
            .Include<SchoolUser>(u => teacher = u).On(r => r.TeacherId, u => u.StaffId)
            .Where(r => r.RoomCode == "AA-1");

        _testOutputHelper.WriteLine(query.ToCommand(FetchType.FetchOne).CommandText);

        var classRoom = await query.SingleAsync();

        teacher.ShouldNotBeNull();
        teacher.StaffId.ShouldBe(11);
        teacher.Name.ShouldBe("Teacher-11");

        classRoom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_string_one_to_one()
    {
        await InsertSeedData();

        Classroom? homeRoom = null;

        var query = theSession
            .Query<SchoolUser>()
            .Include<Classroom>(c => homeRoom = c).On(u => u.HomeRoom, c => c.RoomCode)
            .Where(u => u.Name == "Student-1002");

        _testOutputHelper.WriteLine(query.ToCommand(FetchType.FetchOne).CommandText);

        var classroom = await query.SingleAsync();

        homeRoom.ShouldNotBeNull();
        homeRoom.RoomCode.ShouldBe("AA-2");

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_one_to_many_list()
    {
        await InsertSeedData();

        var students = new List<SchoolUser>();

        var query = theSession
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom)
            .Where(r => r.RoomCode == "AA-1");

        _testOutputHelper.WriteLine(query.ToCommand(FetchType.FetchOne).CommandText);

        var classroom = await query.SingleAsync();

        students.ShouldAllBe(s => s.HomeRoom == "AA-1");
        students.Count.ShouldBe(25);
        students.ShouldBeUnique();

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_list()
    {
        await InsertSeedData();

        var students = new List<SchoolUser>();

        var query = theSession
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var classrooms = await query.ToListAsync();

        students.Count.ShouldBe(100);
        students.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_many_to_one_list()
    {
        await InsertSeedData();

        var homeRooms = new List<Classroom>();

        var query = theSession
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode)
            .Where(s => s.StaffId == null);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(4);
        students.ShouldBeUnique();

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_string_many_to_one_dictionary()
    {
        await InsertSeedData();

        var homeRooms = new Dictionary<string, Classroom>();

        var query = theSession
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode)
            .Where(s => s.StaffId == null);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(4);
        homeRooms.ShouldAllBe(kvp => kvp.Key == kvp.Value.RoomCode);

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_int_many_to_one_dictionary()
    {
        await InsertSeedData();

        var teachers = new Dictionary<int, SchoolUser>();

        var query = theSession
            .Query<Classroom>()
            .Include(teachers).On(r => r.TeacherId, u => u.StaffId);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var classrooms = await query.ToListAsync();

        teachers.Count.ShouldBe(5);
        teachers.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_dictionary()
    {
        await InsertSeedData();

        var students = new Dictionary<string, IList<SchoolUser>>();

        var query = theSession
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var classRoom = await query.ToListAsync();

        students.Count.ShouldBe(4);
        students.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        students.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        students.SelectMany(kvp => kvp.Value).Count().ShouldBe(100);

        classRoom.ShouldNotBeNull();
        classRoom.Count.ShouldBe(5);
    }

    // filters

    [Fact]
    public async Task include_for_a_single_mapped_document_int_one_to_one_filtered()
    {
        await InsertSeedData();

        SchoolUser? teacher = null;

        var query = theSession
            .Query<Classroom>()
            .Include<SchoolUser>(u => teacher = u).On(r => r.TeacherId, u => u.StaffId, u => u.Name == "Invalid")
            .Where(r => r.RoomCode == "AA-1");

        _testOutputHelper.WriteLine(query.ToCommand(FetchType.FetchOne).CommandText);

        var classRoom = await query.SingleAsync();

        teacher.ShouldBeNull();
        classRoom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_string_one_to_one_filtered()
    {
        await InsertSeedData();

        Classroom? homeRoom = null;

        var query = theSession
            .Query<SchoolUser>()
            .Include<Classroom>(c => homeRoom = c)
            .On(u => u.HomeRoom, c => c.RoomCode, c => c.RoomCode == "Invalid")
            .Where(u => u.Name == "Student-1002");

        _testOutputHelper.WriteLine(query.ToCommand(FetchType.FetchOne).CommandText);

        var classroom = await query.SingleAsync();

        homeRoom.ShouldBeNull();
        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_a_single_mapped_document_one_to_many_list_filtered()
    {
        await InsertSeedData();

        var students = new List<SchoolUser>();

        var query = theSession
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"))
            .Where(r => r.RoomCode == "AA-1");

        _testOutputHelper.WriteLine(query.ToCommand(FetchType.FetchOne).CommandText);

        var classroom = await query.SingleAsync();

        students.ShouldAllBe(s => s.HomeRoom == "AA-1");
        students.Count.ShouldBe(5);
        students.ShouldBeUnique();

        classroom.ShouldNotBeNull();
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_list_filtered()
    {
        await InsertSeedData();

        var students = new List<SchoolUser>();

        var query = theSession
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"));

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var classrooms = await query.ToListAsync();

        students.Count.ShouldBe(10);
        students.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_many_to_one_list_filtered()
    {
        await InsertSeedData();

        var homeRooms = new List<Classroom>();

        var query = theSession
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode, r => r.TeacherId < 13)
            .Where(s => s.StaffId == null);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(3);
        students.ShouldBeUnique();

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_string_many_to_one_dictionary_filtered()
    {
        await InsertSeedData();

        var homeRooms = new Dictionary<string, Classroom>();

        var query = theSession
            .Query<SchoolUser>()
            .Include(homeRooms).On(u => u.HomeRoom, r => r.RoomCode, r => r.TeacherId < 13)
            .Where(s => s.StaffId == null);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var students = await query.ToListAsync();

        homeRooms.Count.ShouldBe(3);
        homeRooms.ShouldAllBe(kvp => kvp.Key == kvp.Value.RoomCode);

        students.ShouldNotBeNull();
        students.Count.ShouldBe(100);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_int_many_to_one_dictionary_filtered()
    {
        await InsertSeedData();

        var teachers = new Dictionary<int, SchoolUser>();

        var query = theSession
            .Query<Classroom>()
            .Include(teachers).On(r => r.TeacherId, u => u.StaffId, u => u.StaffId < 13);

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var classrooms = await query.ToListAsync();

        teachers.Count.ShouldBe(3);
        teachers.ShouldBeUnique();

        classrooms.ShouldNotBeNull();
        classrooms.Count.ShouldBe(5);
    }

    [Fact]
    public async Task include_for_multiple_mapped_document_one_to_many_dictionary_filtered()
    {
        await InsertSeedData();

        var students = new Dictionary<string, IList<SchoolUser>>();

        var query = theSession
            .Query<Classroom>()
            .Include(students).On(r => r.RoomCode, u => u.HomeRoom, u => u.Name.EndsWith("1"));

        _testOutputHelper.WriteLine(query.ToCommand().CommandText);

        var classRoom = await query.ToListAsync();

        students.Count.ShouldBe(2);
        students.SelectMany(kvp => kvp.Value.Select(v => (k: kvp.Key, v)))
            .ShouldAllBe(kvp => kvp.k.Equals(kvp.v.HomeRoom));
        students.SelectMany(kvp => kvp.Value).ShouldBeUnique();
        students.SelectMany(kvp => kvp.Value).Count().ShouldBe(10);

        classRoom.ShouldNotBeNull();
        classRoom.Count.ShouldBe(5);
    }

    private Task InsertSeedData()
    {
        List<object> documents =
        [
            ..Enumerable.Range(0, 5).Select(i => new Classroom(CombGuidIdGeneration.NewGuid(), $"AA-{i}", 10 + i)),
            ..Enumerable.Range(10, 10)
                .Select(i => new SchoolUser(CombGuidIdGeneration.NewGuid(), i, $"Teacher-{i}", null)),
            ..Enumerable.Range(1000, 100)
                .Select(i => new SchoolUser(CombGuidIdGeneration.NewGuid(), null, $"Student-{i}", $"AA-{i % 4}")),
        ];

        return theStore.BulkInsertDocumentsAsync(documents);
    }

    #region Test Models

    // alias is to avoid long name issues
    [DocumentAlias("school_user")]
    public record SchoolUser(Guid Id, int? StaffId, string Name, string? HomeRoom);
    [DocumentAlias("classroom")]
    public record Classroom(Guid Id, string RoomCode, int TeacherId);

    #endregion
}

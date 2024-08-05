using Marten;
using Marten.Pagination;
using Weasel.Core;

const string connectionString = "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5";

var store = DocumentStore.For(
    options =>
    {
        options.Connection(connectionString);
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
);

await using var theSession = store.QuerySession();

var userInfo = new Dictionary<string, UserInformation3351>();
var users = await theSession
    .Query<User3351>()
    .Include(x => x.Id, userInfo)
    .ToPagedListAsync(1, 1); // This does not

Console.WriteLine(users.Count());

public class User3351
{
    public string? Id { get; set; }
    public string? FirstName { get; set; }
}

public class UserInformation3351
{
    public string? Id { get; set; }
    public string? Company { get; set; }
}

using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace Marten.Testing.Documents;

#region sample_NoSetterDocument

public class NoSetterDocument
{
    public NoSetterDocument(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
}

#endregion

public class Friend
{
    public string Name { get; set; }
}

public class User
{
    public User()
    {
        Id = Guid.NewGuid();
    }

    public List<Friend> Friends { get; set; }

    public string[] Roles { get; set; }
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    public string? Nickname { get; set; }
    public bool Internal { get; set; }
    public string Department { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public int Age { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public string ToJson()
    {
        return
            $"{{\"Id\": \"{Id}\", \"Age\": {Age}, \"FullName\": \"{FullName}\", \"Internal\": {Internal.ToString().ToLowerInvariant()}, \"LastName\": \"{LastName}\", \"UserName\": \"{UserName}\", \"FirstName\": \"{FirstName}\", \"Department\": \"{Department}\"}}";
    }

    public void From(User user)
    {
        Id = user.Id;
    }

    public override string ToString()
    {
        return $"{nameof(FirstName)}: {FirstName}, {nameof(LastName)}: {LastName}";
    }
}

public class SuperUser: User
{
    public string Role { get; set; }
}

public class AdminUser: User
{
    public string Region { get; set; }
}

public class UserWithPrivateId
{
    public Guid Id { get; private set; }

    public string UserName { get; set; }
}

public class UserWithoutIdSetter
{
    public UserWithoutIdSetter()
    {
    }

    public Guid Id { get; }

    public string UserName { get; set; }
}

public class UserWithInterface: User, IUserWithInterface
{
}

public interface IUserWithInterface
{
    Guid Id { get; set; }
    string UserName { get; set; }
}

public class UserWithNicknames
{
    public string[] Nicknames { get; set; }
}

public class UserWithReadonlyCollectionWithPrivateSetter
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public IReadOnlyCollection<int> Collection { get; private set; }

    public UserWithReadonlyCollectionWithPrivateSetter(Guid id, string name, IReadOnlyCollection<int> collection)
    {
        Id = id;
        Name = name;
        Collection = collection.ToList();
    }
}

public class Post
{
    public Guid Id { get; set; }
    public string[] Tags { get; set; }
}

public record FriendCount(Guid Id, int Number);

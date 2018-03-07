using System;
using Baseline;

namespace Marten.Testing.Documents
{
    public class User
    {
        public User()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string UserName { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public bool Internal { get; set; }

        public string FullName => "{0} {1}".ToFormat(FirstName, LastName);
        
        public int Age { get; set; }

        public string ToJson()
        {
            return $"{{\"Id\": \"{Id}\", \"Age\": {Age}, \"FullName\": \"{FullName}\", \"Internal\": {Internal.ToString().ToLowerInvariant()}, \"LastName\": \"{LastName}\", \"UserName\": \"{UserName}\", \"FirstName\": \"{FirstName}\"}}";
        }

        public void From(User user)
        {
            Id = user.Id;
        }
    }

    public class SuperUser : User
    {
        public string Role { get; set; }
    }

    public class AdminUser : User
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

    public class UserWithInterface : User, IUserWithInterface
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
}
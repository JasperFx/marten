using System;

namespace DinnerParty.Models
{
    public class UserModel
    {
        public Guid Id { get; set; }
        public string Username { get; set; }
        public string FriendlyName { get; set; }
        public string EMailAddress { get; set; }
        public string LoginType { get; set; }
        public string Password { get; set; }
    }
}
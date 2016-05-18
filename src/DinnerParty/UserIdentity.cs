using System;
using System.Collections.Generic;
using Nancy.Security;

namespace DinnerParty
{
    public class UserIdentity : IUserIdentity
    {
        public IEnumerable<string> Claims
        {
            get;
            set;
        }

        public string UserName
        {
            get;
            set;
        }

        public string FriendlyName { get; set; }

      
    }
}
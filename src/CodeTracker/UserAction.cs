using System;

namespace CodeTracker
{
    public class UserAction
    {
        public UserAction()
        {
        }

        public UserAction(string userName, DateTimeOffset timestamp)
        {
            UserName = userName;
            Timestamp = timestamp;
        }

        public string UserName { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
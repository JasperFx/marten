//using Octokit;

namespace Marten.Testing.Github
{
    public class GhUser
    {
        public GhUser()
        {
        }

        /*
        public GhUser(Author account)
        {
            AvatarUrl = account.AvatarUrl;
            HtmlUrl = account.HtmlUrl;
            Id = account.Id;
            Login = account.Login;
            Type = account.Type;
            Url = account.Url;
        }
        */

        public string Type { get; set; }

        public string Url { get; set; }

        public string Login { get; set; }

        public int Id { get; set; }

        public string HtmlUrl { get; set; }

        public string AvatarUrl { get; set; }
    }
}
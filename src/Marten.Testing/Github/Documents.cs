using System;
using System.Linq;
//using Octokit;

namespace Marten.Testing.Github
{
    public class GhCommit
    {
        public GhCommit()
        {
        }
        /*
        public GhCommit(GitHubCommit commit)
        {
            Message = commit.Commit.Message;
            if (commit.Author != null) Author = commit.Author.Id;
            if (commit.Files != null) Files = commit.Files.Select(x => new CommitFile(x)).ToArray();

            Additions = commit.Stats.Additions;
            Deletions = commit.Stats.Deletions;
            Total = commit.Stats.Total;
        }
        */

        public int Total { get; set; }

        public int Deletions { get; set; }

        public int Additions { get; set; }

        public int Author { get; set; }

        public string Message { get; set; }

        public CommitFile[] Files { get; set; }

        public Comment[] Comments { get; set; }
    }

    public class Comment
    {
        public Comment()
        {
        }

        public Comment(CommitComment comment)
        {
            Id = comment.Id;
            Url = comment.Url;
            Body = comment.Body;
            User = comment.User.Id;
        }

        public int User { get; set; }

        public string Body { get; set; }

        public Uri Url { get; set; }

        public int Id { get; set; }
    }

    public class CommitFile
    {
        public CommitFile()
        {
        }

        public CommitFile(GitHubCommitFile file)
        {
            Filename = file.Filename;
            Additions = file.Additions;
            Deletions = file.Deletions;
            Changes = file.Changes;
            Status = file.Status;
            BlobUrl = file.BlobUrl;
            ContentsUrl = file.ContentsUrl;
            RawUrl = file.RawUrl;
            Sha = file.Sha;
            PreviousFileName = file.PreviousFileName;
        }

        public string PreviousFileName { get; set; }

        public string Sha { get; set; }

        public string RawUrl { get; set; }

        public string ContentsUrl { get; set; }

        public string BlobUrl { get; set; }

        public string Status { get; set; }

        public int Changes { get; set; }

        public int Deletions { get; set; }

        public int Additions { get; set; }

        public string Filename { get; set; }
    }
}
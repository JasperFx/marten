using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DinnerParty.Models;
using Marten;
using Nancy;
using Nancy.Authentication.Forms;
using Nancy.Security;

namespace DinnerParty
{
    public class UserMapper : IUserMapper
    {
        private readonly IDocumentSession _documentSession;

        public UserMapper(IDocumentSession documentSession)
        {
            _documentSession = documentSession;

        }

        public IUserIdentity GetUserFromIdentifier(Guid identifier, NancyContext context)
        {
            var userRecord = _documentSession.Query<UserModel>().FirstOrDefault(x => x.Id == identifier);

            return userRecord == null ? null : new UserIdentity() { UserName = userRecord.Username, FriendlyName = userRecord.FriendlyName };
        }

        public Guid? ValidateUser(string username, string password)
        {
            var userRecord = _documentSession.Query<UserModel>().FirstOrDefault(x => x.Username == username && x.Password == EncodePassword(password));

            return userRecord?.Id;
        }

        public Guid? ValidateRegisterNewUser(RegisterModel newUser)
        {
            var userRecord = new UserModel()
            {
                Id = Guid.NewGuid(),
                LoginType = "DinnerParty",
                EMailAddress = newUser.Email,
                FriendlyName = newUser.Name,
                Username = newUser.UserName,
                Password = EncodePassword(newUser.Password)
            };

            var existingUser = _documentSession.Query<UserModel>().FirstOrDefault(x => x.EMailAddress == userRecord.EMailAddress && x.LoginType == "DinnerParty");
            if (existingUser != null)
                return null;

            _documentSession.Store(userRecord);
            _documentSession.SaveChanges();

            return userRecord.Id;
        }

        private string EncodePassword(string originalPassword)
        {
            if (originalPassword == null)
                return String.Empty;

            //Declarations

            //Instantiate MD5CryptoServiceProvider, get bytes for original password and compute hash (encoded password)
            MD5 md5 = new MD5CryptoServiceProvider();
            var originalBytes = Encoding.Default.GetBytes(originalPassword);
            var encodedBytes = md5.ComputeHash(originalBytes);

            //Convert encoded bytes back to a 'readable' string
            return BitConverter.ToString(encodedBytes);
        }

    }
}
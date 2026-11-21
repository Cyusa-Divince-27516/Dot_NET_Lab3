namespace Cyusa_Divince_Lab3.Controllers
{
    public sealed class AuthenticationController
    {
        public Staff Login(string username, string password)
        {
            return Database.Login(username, password);
        }
    }
}

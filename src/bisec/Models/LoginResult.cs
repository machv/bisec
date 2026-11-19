namespace BiSec.Library.Models
{
    public class LoginResult
    {
        /// <summary>
        /// The gateway's id for the logged-in user; 0 is the administrator.
        /// </summary>
        public int UserId { get; }
        public string Token { get; }
        public bool IsAdmin => UserId == GatewayLimits.AdminUserId;

        public LoginResult(int userId, string token)
        {
            UserId = userId;
            Token = token;
        }

        public override string ToString()
        {
            return $"LoginResult {{UserId = {UserId}, IsAdmin = {IsAdmin} }}";
        }
    }
}

using System;
using System.Text.RegularExpressions;

namespace BiSec.Library
{
    [Flags]
    public enum PasswordIssues
    {
        None = 0,
        TooShort = 1,
        TooLong = 2,
        NoUpperCase = 4,
        NoLowerCase = 8,
        NoDigit = 16,
        NoSpecialCharacter = 32,
    }

    /// <summary>
    /// The legacy app's input rules (StringValidator). The library does not enforce them;
    /// the app itself only warns about weak passwords after login.
    /// </summary>
    public static class GatewayValidation
    {
        public static bool IsValidUserName(string userName)
        {
            return userName != null && userName.Length >= 2 && userName.Length <= 20;
        }

        public static bool IsValidGroupName(string groupName)
        {
            return groupName != null && groupName.Length >= 1 && groupName.Length <= 20;
        }

        public static bool IsValidGatewayName(string gatewayName)
        {
            return gatewayName != null && gatewayName.Length >= 2 && gatewayName.Length <= 20;
        }

        public static PasswordIssues CheckPassword(string password)
        {
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            PasswordIssues issues = PasswordIssues.None;

            if (password.Length < 10)
                issues |= PasswordIssues.TooShort;

            if (password.Length > 20)
                issues |= PasswordIssues.TooLong;

            if (!Regex.IsMatch(password, "[A-Z]"))
                issues |= PasswordIssues.NoUpperCase;

            if (!Regex.IsMatch(password, "[a-z]"))
                issues |= PasswordIssues.NoLowerCase;

            if (!Regex.IsMatch(password, "\\d", RegexOptions.ECMAScript))
                issues |= PasswordIssues.NoDigit;

            if (!Regex.IsMatch(password, "[^A-Za-z\\d\\s\u00e4-\u00fc\u00c4-\u00dc]", RegexOptions.ECMAScript))
                issues |= PasswordIssues.NoSpecialCharacter;

            return issues;
        }
    }
}

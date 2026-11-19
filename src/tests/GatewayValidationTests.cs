using BiSec.Library;
using System;
using Xunit;

namespace tests
{
    public class GatewayValidationTests
    {
        [Theory]
        [InlineData("Abcdefg1!x", PasswordIssues.None)]
        // Exactly 20 characters is still allowed.
        [InlineData("Abcdefghij1!Abcdefgh", PasswordIssues.None)]
        [InlineData("Abcdefg1!", PasswordIssues.TooShort)]
        [InlineData("Abcdefghij1!Abcdefghi", PasswordIssues.TooLong)]
        [InlineData("abcdefg1!x", PasswordIssues.NoUpperCase)]
        [InlineData("ABCDEFG1!X", PasswordIssues.NoLowerCase)]
        [InlineData("Abcdefgh!x", PasswordIssues.NoDigit)]
        [InlineData("Abcdefg123", PasswordIssues.NoSpecialCharacter)]
        // Neither a space nor an umlaut counts as a special character.
        [InlineData("Abcdefg1 2", PasswordIssues.NoSpecialCharacter)]
        [InlineData("Abcdefg12\u00E4", PasswordIssues.NoSpecialCharacter)]
        [InlineData("abc", PasswordIssues.TooShort | PasswordIssues.NoUpperCase | PasswordIssues.NoDigit | PasswordIssues.NoSpecialCharacter)]
        [InlineData("", PasswordIssues.TooShort | PasswordIssues.NoUpperCase | PasswordIssues.NoLowerCase | PasswordIssues.NoDigit | PasswordIssues.NoSpecialCharacter)]
        public void CheckPasswordReportsEveryRuleThePasswordBreaks(string password, PasswordIssues expected)
        {
            Assert.Equal(expected, GatewayValidation.CheckPassword(password));
        }

        [Fact]
        public void CheckPasswordRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => GatewayValidation.CheckPassword(null));
        }

        [Theory]
        [InlineData("a", false)]
        [InlineData("ab", true)]
        [InlineData("abcdefghijklmnopqrst", true)]
        [InlineData("abcdefghijklmnopqrstu", false)]
        [InlineData(null, false)]
        public void UserAndGatewayNamesNeedTwoToTwentyCharacters(string name, bool valid)
        {
            Assert.Equal(valid, GatewayValidation.IsValidUserName(name));
            Assert.Equal(valid, GatewayValidation.IsValidGatewayName(name));
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("a", true)]
        [InlineData("abcdefghijklmnopqrst", true)]
        [InlineData("abcdefghijklmnopqrstu", false)]
        public void GroupNamesNeedOneToTwentyCharacters(string name, bool valid)
        {
            Assert.Equal(valid, GatewayValidation.IsValidGroupName(name));
        }
    }
}

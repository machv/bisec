using BiSec.Library;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    public class GatewayClientUsersTests
    {
        private static byte[] Bytes(byte first, string text)
        {
            return new[] { first }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        }

        [Fact]
        public async Task GetUsersReturnsTheUsersOfTheGateway()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            User[] users = await session.Client.GetUsersAsync();

            Assert.Equal("{\"cmd\":\"GET_USERS\"}", Assert.Single(session.Gateway.Requests).Text);
            Assert.Equal(2, users.Length);

            Assert.Equal(0, users[0].Id);
            Assert.Equal("admin", users[0].Name);
            Assert.True(users[0].IsAdmin);
            Assert.Empty(users[0].Groups);

            Assert.Equal(GatewayTestSession.UserId, users[1].Id);
            Assert.Equal("anna", users[1].Name);
            Assert.False(users[1].IsAdmin);
            Assert.Equal(new[] { 1 }, users[1].Groups);
        }

        [Fact]
        public async Task AddUserSendsNameAndPasswordAndReturnsTheNewId()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            int userId = await session.Client.AddUserAsync("ben", "Ben-secret-1");

            // Ids 0 and 3 are taken, so the fake hands out 1.
            Assert.Equal(1, userId);
            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.AddUser, request.Command);
            Assert.Equal(Bytes(3, "benBen-secret-1"), request.Payload);
            Assert.Equal("Ben-secret-1", session.Gateway.User("ben").Password);
        }

        [Fact]
        public async Task RemoveUserSendsTheUserId()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.RemoveUserAsync(GatewayTestSession.UserId);

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.RemoveUser, request.Command);
            Assert.Equal(new byte[] { 3 }, request.Payload);
            Assert.Equal(new[] { "admin" }, session.Gateway.Users.Select(u => u.Name));
        }

        [Fact]
        public async Task GetUserRightsReturnsTheGroupIdsWithoutTheLeadingUserId()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.User("anna").Groups.Add(0);

            int[] groupIds = await session.Client.GetUserRightsAsync(GatewayTestSession.UserId);

            Assert.Equal(new[] { 1, 0 }, groupIds);
            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.GetUserRights, request.Command);
            Assert.Equal(new byte[] { 3 }, request.Payload);
        }

        [Fact]
        public async Task SetUserRightsSendsTheUserIdFollowedByTheGroupIds()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.SetUserRightsAsync(GatewayTestSession.UserId, new[] { 0, 1 });

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.SetUserRights, request.Command);
            Assert.Equal(new byte[] { 3, 0, 1 }, request.Payload);
            Assert.Equal(new[] { 0, 1 }, session.Gateway.User("anna").Groups);
        }

        [Fact]
        public async Task RenamingOneselfSendsChangeUserName()
        {
            using var session = await GatewayTestSession.AsUserAsync();

            await session.Client.ChangeUserNameAsync(GatewayTestSession.UserId, "anne");

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.ChangeUserName, request.Command);
            Assert.Equal("anne", request.Text);
            Assert.Equal("anne", session.Gateway.Users.Single(u => u.Id == GatewayTestSession.UserId).Name);
        }

        [Fact]
        public async Task RenamingAnotherUserAsAdministratorSendsChangeUserNameOfUser()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.ChangeUserNameAsync(GatewayTestSession.UserId, "anne");

            Assert.Equal(new[] { "GET_GW_VERSION", "CHANGE_USER_NAME_OF_USER" }, session.Gateway.RequestNames);
            Assert.Equal(Bytes(3, "anne"), session.Gateway.Requests[1].Payload);
            Assert.Equal("anne", session.Gateway.Users.Single(u => u.Id == GatewayTestSession.UserId).Name);
            Assert.Equal("admin", session.Gateway.Users.Single(u => u.Id == 0).Name);
        }

        [Fact]
        public async Task ChangingOnesOwnPasswordSendsChangePassword()
        {
            using var session = await GatewayTestSession.AsUserAsync();

            await session.Client.ChangePasswordAsync(GatewayTestSession.UserId, "New-secret-1");

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.ChangePassword, request.Command);
            Assert.Equal("New-secret-1", request.Text);
            Assert.Equal("New-secret-1", session.Gateway.User("anna").Password);
        }

        [Fact]
        public async Task ChangingAnotherUsersPasswordAsAdministratorSendsChangePasswordOfUser()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.ChangePasswordAsync(GatewayTestSession.UserId, "New-secret-1");

            Assert.Equal(new[] { "GET_GW_VERSION", "CHANGE_PASSWORD_OF_USER" }, session.Gateway.RequestNames);
            Assert.Equal(Bytes(3, "New-secret-1"), session.Gateway.Requests[1].Payload);
            Assert.Equal("New-secret-1", session.Gateway.User("anna").Password);
            Assert.Equal(GatewayTestSession.AdminPassword, session.Gateway.User("admin").Password);
        }

        [Fact]
        public async Task ReloginAfterChangingOnesOwnNameAndPasswordUsesTheNewCredentials()
        {
            using var session = await GatewayTestSession.AsUserAsync();
            await session.Client.ChangeUserNameAsync(GatewayTestSession.UserId, "anne");
            await session.Client.ChangePasswordAsync(GatewayTestSession.UserId, "New-secret-1");
            session.Gateway.ClearRequests();
            session.Gateway.ExpireSession();

            await session.Client.GetUsersAsync();

            Assert.Equal(new[] { "JMCP", "LOGIN", "JMCP" }, session.Gateway.RequestNames);
            Assert.Equal(Bytes(4, "anneNew-secret-1"), session.Gateway.Requests[1].Payload);
            Assert.Equal(GatewayTestSession.UserId, session.Client.UserId);
        }

        [Fact]
        public async Task NonAdministratorEditingAnotherUserIsRefusedWithoutSending()
        {
            using var session = await GatewayTestSession.AsUserAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Client.ChangeUserNameAsync(0, "root"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Client.ChangePasswordAsync(0, "New-secret-1"));
            await session.Client.PingAsync();

            Assert.Equal(new[] { "PING" }, session.Gateway.RequestNames);
            Assert.Equal("admin", session.Gateway.Users.Single(u => u.Id == 0).Name);
        }

        [Theory]
        [InlineData("EE001425-06")]
        [InlineData("EE001425-09")]
        // Firmware up to EE001425-06 does not know GET_GW_VERSION at all.
        [InlineData(null)]
        public async Task FirmwareBelowEe001425_10CannotEditOtherUsers(string firmwareVersion)
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FirmwareVersion = firmwareVersion;

            await Assert.ThrowsAsync<NotSupportedException>(() => session.Client.ChangeUserNameAsync(GatewayTestSession.UserId, "anne"));
            await Assert.ThrowsAsync<NotSupportedException>(() => session.Client.ChangePasswordAsync(GatewayTestSession.UserId, "New-secret-1"));

            // The version is asked for once and then remembered.
            Assert.Equal(new[] { "GET_GW_VERSION" }, session.Gateway.RequestNames);
            Assert.Equal("anna", session.Gateway.Users.Single(u => u.Id == GatewayTestSession.UserId).Name);
        }

        [Fact]
        public async Task SwitchingTheFirmwareGateOffSendsTheRequestAnyway()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FirmwareVersion = "EE001425-06";
            session.Client.EnforceFirmwareFeatureGates = false;

            await session.Client.ChangeUserNameAsync(GatewayTestSession.UserId, "anne");
            await session.Client.ChangePasswordAsync(GatewayTestSession.UserId, "New-secret-1");

            Assert.Equal(new[] { "CHANGE_USER_NAME_OF_USER", "CHANGE_PASSWORD_OF_USER" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task EditingAUserNeedsALogin()
        {
            using var session = new GatewayTestSession();

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.ChangeUserNameAsync(0, "root"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.ChangePasswordAsync(0, "New-secret-1"));
            await session.Client.PingAsync();

            Assert.Equal(new[] { "PING" }, session.Gateway.RequestNames);
        }
    }
}

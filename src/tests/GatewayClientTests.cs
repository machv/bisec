using BiSec.Library;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    /// <summary>
    /// Session handling of <see cref="GatewayClient"/>: login, logout, re-login, session end and
    /// how answers that are not the expected data surface.
    /// </summary>
    public class GatewayClientTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task LoginReturnsUserIdAdminFlagAndToken()
        {
            using var session = new GatewayTestSession();

            LoginResult admin = await session.Client.LoginAsync(GatewayTestSession.AdminName, GatewayTestSession.AdminPassword);

            Assert.Equal(0, admin.UserId);
            Assert.True(admin.IsAdmin);
            Assert.NotEqual(FakeGateway.NoToken, admin.Token);
            Assert.Equal(session.Gateway.SessionToken, admin.Token);
            Assert.True(session.Client.IsLoggedIn);
            Assert.True(session.Client.IsAdmin);
            Assert.Equal(0, session.Client.UserId);

            LoginResult user = await session.Client.LoginAsync(GatewayTestSession.UserName, GatewayTestSession.UserPassword);

            Assert.Equal(GatewayTestSession.UserId, user.UserId);
            Assert.False(user.IsAdmin);
            Assert.NotEqual(admin.Token, user.Token);
            Assert.Equal(session.Gateway.SessionToken, user.Token);
            Assert.True(session.Client.IsLoggedIn);
            Assert.False(session.Client.IsAdmin);
            Assert.Equal(GatewayTestSession.UserId, session.Client.UserId);
        }

        [Fact]
        public async Task GetTokenReturnsTheSessionToken()
        {
            using var session = new GatewayTestSession();

            string token = await session.Client.GetToken(GatewayTestSession.AdminName, GatewayTestSession.AdminPassword);

            Assert.Equal(session.Gateway.SessionToken, token);
            Assert.NotEqual(FakeGateway.NoToken, token);
        }

        [Fact]
        public async Task WrongPasswordThrowsLoginFailedAndLeavesTheClientLoggedOut()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(
                () => session.Client.LoginAsync(GatewayTestSession.AdminName, "wrong"));

            Assert.Equal(Error.LoginFailed, e.Error);
            Assert.Equal(Command.Login, e.Command);
            Assert.False(session.Client.IsLoggedIn);
            Assert.Null(session.Client.UserId);
            Assert.False(session.Connection.IsAuthenticated);
        }

        [Fact]
        public async Task NonAsciiUserNameIsFramedWithItsUtf8ByteLength()
        {
            const string name = "j\u00F6rg";
            const string password = "p\u00E4ssword";

            using var session = new GatewayTestSession();
            FakeUser user = session.Gateway.AddUser(name, password);

            LoginResult result = await session.Client.LoginAsync(name, password);

            // Four characters, five bytes.
            byte[] expected = new byte[] { 5 }
                .Concat(Encoding.UTF8.GetBytes(name))
                .Concat(Encoding.UTF8.GetBytes(password))
                .ToArray();

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(expected, request.Payload);
            Assert.Equal(user.Id, result.UserId);
        }

        [Fact]
        public async Task TruncatedLoginResponseIsAProtocolErrorAndDoesNotLogIn()
        {
            using var session = new GatewayTestSession();
            session.Gateway.ReplyNext(Command.Login, Command.Login, 0x00, 0x01);

            await Assert.ThrowsAsync<GatewayProtocolException>(
                () => session.Client.LoginAsync(GatewayTestSession.AdminName, GatewayTestSession.AdminPassword));

            Assert.False(session.Client.IsLoggedIn);
        }

        [Fact]
        public async Task AddUserWithAnExistingNameThrowsInsteadOfReturningTheErrorCodeAsUserId()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(
                () => session.Client.AddUserAsync(GatewayTestSession.UserName, "Another-pass1"));

            Assert.Equal(Error.UserAlreadyExists, e.Error);
            Assert.Equal(Command.AddUser, e.Command);
            Assert.Equal(2, session.Gateway.Users.Count);
        }

        [Fact]
        public async Task GetUsersThrowsWhenTheGatewayAnswersWithAnError()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FailNext(Command.Jmcp, Error.GatewayBusy);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetUsersAsync());

            Assert.Equal(Error.GatewayBusy, e.Error);
            Assert.Equal(Command.Jmcp, e.Command);
        }

        [Fact]
        public async Task GetNameThrowsWhenTheGatewayAnswersWithAnError()
        {
            using var session = new GatewayTestSession();
            session.Gateway.FailNext(Command.GetName, Error.GatewayBusy);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetNameAsync());

            Assert.Equal(Error.GatewayBusy, e.Error);
        }

        [Fact]
        public async Task ErrorAnswerWithoutACodeIsAProtocolError()
        {
            using var session = new GatewayTestSession();
            session.Gateway.ReplyNext(Command.GetName, Command.Error);

            await Assert.ThrowsAsync<GatewayProtocolException>(() => session.Client.GetNameAsync());
        }

        [Fact]
        public async Task AnswerWithAnotherCommandIsAProtocolError()
        {
            using var session = new GatewayTestSession();
            session.Gateway.ReplyNext(Command.GetName, Command.Ping);

            await Assert.ThrowsAsync<GatewayProtocolException>(() => session.Client.GetNameAsync());
        }

        [Fact]
        public async Task UnparseableJsonIsAProtocolError()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.ReplyNext(Command.Jmcp, Command.Jmcp, Encoding.UTF8.GetBytes("[{\"id\":"));

            await Assert.ThrowsAsync<GatewayProtocolException>(() => session.Client.GetUsersAsync());
        }

        [Fact]
        public async Task UnansweredRequestSurfacesAsTimeout()
        {
            using var session = new GatewayTestSession();
            session.Connection.Timeouts.Set(Command.GetName, 250);
            session.Gateway.IgnoreNext(Command.GetName);

            await Assert.ThrowsAsync<TimeoutException>(() => session.Client.GetNameAsync());

            Assert.Equal(session.Gateway.Name, await session.Client.GetNameAsync());
        }

        [Fact]
        public async Task SendRawReturnsAnErrorAnswerAsItIs()
        {
            using var session = new GatewayTestSession();

            // The fake does not implement GET_MAC and answers "unknown command".
            Message response = await session.Client.SendRawAsync(Command.GetMac);

            Assert.True(response.IsError);
            Assert.Equal(Error.CommandNotFound, response.GetError());
        }

        [Fact]
        public async Task ClientCanLogInAgainAfterTheConnectionWasReopened()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Connection.Disconnected += (sender, e) => disconnected.TrySetResult();
            session.Gateway.DropConnection();
            await disconnected.Task.WaitAsync(Guard);

            await session.Connection.ConnectAsync().WaitAsync(Guard);
            Assert.False(session.Client.IsLoggedIn);

            LoginResult result = await session.Client.LoginAsync(GatewayTestSession.UserName, GatewayTestSession.UserPassword);
            User[] users = await session.Client.GetUsersAsync();

            Assert.Equal(GatewayTestSession.UserId, result.UserId);
            Assert.True(session.Client.IsLoggedIn);
            Assert.Equal(2, users.Length);
            Assert.Equal(2, session.Gateway.ConnectionCount);
        }

        [Fact]
        public async Task PermissionDeniedMakesTheClientLogInAgainAndRepeatTheRequest()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            string firstToken = session.Connection.Token;
            session.Gateway.FailNext(Command.Jmcp, Error.PermissionDenied);

            User[] users = await session.Client.GetUsersAsync();

            Assert.Equal(2, users.Length);
            Assert.Equal(new[] { "JMCP", "LOGIN", "JMCP" }, session.Gateway.RequestNames);
            Assert.True(session.Client.IsLoggedIn);
            Assert.Equal(0, session.Client.UserId);

            // The repeated request runs in the new session.
            Assert.NotEqual(firstToken, session.Connection.Token);
            Assert.Equal(session.Connection.Token, session.Gateway.Requests[2].Token);
        }

        [Fact]
        public async Task ExpiredSessionIsRenewedTransparently()
        {
            using var session = await GatewayTestSession.AsUserAsync();
            session.Gateway.ExpireSession();

            User[] users = await session.Client.GetUsersAsync();

            Assert.Equal(2, users.Length);
            Assert.Equal(new[] { "JMCP", "LOGIN", "JMCP" }, session.Gateway.RequestNames);
            Assert.Equal(GatewayTestSession.UserId, session.Gateway.SessionUserId);
        }

        [Fact]
        public async Task PermissionDeniedIsThrownWhenAutoReloginIsOff()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Client.AutoRelogin = false;
            session.Gateway.FailNext(Command.Jmcp, Error.PermissionDenied);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetUsersAsync());

            Assert.Equal(Error.PermissionDenied, e.Error);
            Assert.Equal(new[] { "JMCP" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task RequestThatKeepsBeingDeniedIsRepeatedOnlyOnce()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FailNext(Command.Jmcp, Error.PermissionDenied, times: 3);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetUsersAsync());

            Assert.Equal(Error.PermissionDenied, e.Error);
            Assert.Equal(new[] { "JMCP", "LOGIN", "JMCP" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task LogoutSendsLogoutAndLeavesTheClientLoggedOut()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            bool sessionEnded = false;
            session.Client.SessionEnded += (sender, e) => sessionEnded = true;

            await session.Client.LogoutAsync();

            Assert.False(session.Client.IsLoggedIn);
            Assert.Null(session.Client.UserId);
            Assert.False(session.Connection.IsAuthenticated);
            Assert.Null(session.Gateway.SessionUserId);
            Assert.True(session.Connection.Connected);
            Assert.False(sessionEnded);

            // The credentials are forgotten, so nothing logs in again behind the caller's back.
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.GetUsersAsync());
            await session.Client.PingAsync();

            Assert.Equal(new[] { "LOGOUT", "PING" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task LogoutCompletesWhenTheGatewayNeverAnswersIt()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Connection.Timeouts.Set(Command.Logout, 250);
            session.Gateway.IgnoreNext(Command.Logout);

            await session.Client.LogoutAsync().WaitAsync(Guard);

            Assert.Equal(new[] { "LOGOUT" }, session.Gateway.RequestNames);
            Assert.False(session.Client.IsLoggedIn);
            Assert.False(session.Connection.IsAuthenticated);
            Assert.True(session.Connection.Connected);
        }

        [Fact]
        public async Task SessionEndedIsRaisedOnceWhenTheGatewayPushesALogout()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            int ended = 0;
            var sessionEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Client.SessionEnded += (sender, e) =>
            {
                Interlocked.Increment(ref ended);
                sessionEnded.TrySetResult();
            };

            await session.Gateway.PushAsync(Command.Logout);
            await sessionEnded.Task.WaitAsync(Guard);

            Assert.False(session.Client.IsLoggedIn);
            Assert.Null(session.Client.UserId);

            // The client subscribed to Disconnected first, so it has handled the drop when this handler runs.
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Connection.Disconnected += (sender, e) => disconnected.TrySetResult();

            session.Gateway.DropConnection();
            await disconnected.Task.WaitAsync(Guard);

            Assert.Equal(1, Volatile.Read(ref ended));
        }

        [Fact]
        public async Task SessionEndedIsRaisedWhenTheConnectionDrops()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            var sessionEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Client.SessionEnded += (sender, e) => sessionEnded.TrySetResult();

            session.Gateway.DropConnection();
            await sessionEnded.Task.WaitAsync(Guard);

            Assert.False(session.Client.IsLoggedIn);
            Assert.Null(session.Client.UserId);
        }

        [Fact]
        public async Task SessionEndedIsNotRaisedWhenNobodyWasLoggedIn()
        {
            using var session = new GatewayTestSession();
            await session.Client.PingAsync();

            bool sessionEnded = false;
            session.Client.SessionEnded += (sender, e) => sessionEnded = true;

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Connection.Disconnected += (sender, e) => disconnected.TrySetResult();

            session.Gateway.DropConnection();
            await disconnected.Task.WaitAsync(Guard);

            Assert.False(sessionEnded);
        }
    }
}

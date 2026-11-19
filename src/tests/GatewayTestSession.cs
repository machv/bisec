using BiSec.Library;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace tests
{
    /// <summary>
    /// A fake gateway with a connection and a client attached to it.
    /// </summary>
    public sealed class GatewayTestSession : IDisposable
    {
        public const string AdminName = "admin";
        public const string AdminPassword = "admin-secret";
        public const string UserName = "anna";
        public const string UserPassword = "anna-secret";
        public const int UserId = 3;

        public FakeGateway Gateway { get; }
        public GatewayConnection Connection { get; }
        public GatewayClient Client { get; }

        public GatewayTestSession()
        {
            Gateway = new FakeGateway();
            Connection = CreateConnection(Gateway);
            Client = new GatewayClient(Connection);
        }

        /// <summary>
        /// A connection to the fake that is not connected yet. A broken test should fail within
        /// seconds, so no request waits for the library's defaults of up to 25 seconds.
        /// </summary>
        public static GatewayConnection CreateConnection(FakeGateway gateway)
        {
            GatewayConnection connection = new GatewayConnection(IPAddress.Loopback, null, FakeGateway.GatewayId, gateway.Port);

            connection.Timeouts.Default = 5000;
            foreach (Command command in Command.Commands)
                connection.Timeouts.Set(command, 5000);

            return connection;
        }

        public static Task<GatewayTestSession> AsAdminAsync()
        {
            return LoggedInAsync(AdminName, AdminPassword);
        }

        public static Task<GatewayTestSession> AsUserAsync()
        {
            return LoggedInAsync(UserName, UserPassword);
        }

        /// <summary>
        /// Logs in and forgets the login request, so <see cref="FakeGateway.Requests"/> starts empty.
        /// </summary>
        public static async Task<GatewayTestSession> LoggedInAsync(string userName, string password)
        {
            GatewayTestSession session = new GatewayTestSession();
            try
            {
                await session.Client.LoginAsync(userName, password);
                session.Gateway.ClearRequests();

                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Gateway.Dispose();
        }
    }

    /// <summary>
    /// A loopback port that refuses connections: the socket is bound but does not listen, and
    /// keeping it open stops a fake gateway of another test from getting the same port.
    /// </summary>
    public sealed class RefusingPort : IDisposable
    {
        private readonly Socket _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public RefusingPort()
        {
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        }

        public int Port => ((IPEndPoint)_socket.LocalEndPoint).Port;

        public void Dispose()
        {
            _socket.Dispose();
        }
    }
}

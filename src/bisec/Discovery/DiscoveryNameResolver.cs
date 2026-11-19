using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    /// <summary>
    /// Fills in the names of discovered gateways. GET_NAME needs no login.
    /// </summary>
    public static class DiscoveryNameResolver
    {
        /// <summary>
        /// Asks the gateway for its name and stores it in <see cref="DiscoveryResult.Name"/>.
        /// Returns null, leaving the name unset, when the gateway cannot be reached or does not answer.
        /// </summary>
        public static Task<string> ResolveNameAsync(DiscoveryResult gateway, ILogger logger = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return ResolveNameAsync(gateway, GatewayConnection.DefaultPort, logger, cancellationToken);
        }

        public static async Task ResolveNamesAsync(IEnumerable<DiscoveryResult> gateways, ILogger logger = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (gateways == null)
                throw new ArgumentNullException(nameof(gateways));

            foreach (DiscoveryResult gateway in gateways)
                await ResolveNameAsync(gateway, logger, cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<string> ResolveNameAsync(DiscoveryResult gateway, int port, ILogger logger, CancellationToken cancellationToken)
        {
            if (gateway == null)
                throw new ArgumentNullException(nameof(gateway));

            using (GatewayConnection connection = new GatewayConnection(gateway.SourceAddress, null, gateway.GatewayId, port))
            {
                connection.SetLogger(logger);

                try
                {
                    await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    Message request = new Message
                    {
                        Command = Command.GetName,
                        Payload = PayloadFactory.Empty,
                    };
                    Package response = await connection.SendAsync(request, cancellationToken).ConfigureAwait(false);

                    if (response.Message.Command.Code != Command.GetName.Code)
                    {
                        logger?.LogInformation($"Gateway {gateway.GatewayId} answered GET_NAME with {response.Message.Command.Name}.");
                        return null;
                    }

                    gateway.Name = response.Message.Payload.TextContent;

                    return gateway.Name;
                }
                catch (Exception e) when (e is TimeoutException || e is IOException || e is SocketException)
                {
                    logger?.LogInformation($"Reading the name of gateway {gateway.GatewayId} failed: {e.Message}");
                    return null;
                }
            }
        }
    }
}

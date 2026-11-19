using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public partial class GatewayClient
    {
        // The app's transition collector gives up after its third failed request (StateContext.MAX_REQUEST_RETRYS = 2).
        private const int TransitionAttempts = 3;
        private const int TransitionRetryDelay = 100;

        /// <summary>
        /// Requests the state reported through one port.
        /// </summary>
        public async Task<Transition> GetTransitionAsync(int portId, CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.GetTransition, PayloadFactory.GetTransition(portId), cancellationToken).ConfigureAwait(false);

            return ParseTransition(response);
        }

        public Task<Transition> GetTransitionAsync(Port port, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (port == null)
                throw new ArgumentNullException(nameof(port));

            return GetTransitionAsync(port.Id, cancellationToken);
        }

        /// <summary>
        /// Requests a group's state through its requestable port, repeating a failed request like the app does.
        /// Throws <see cref="InvalidOperationException"/> when the group cannot report its state.
        /// </summary>
        public async Task<Transition> GetTransitionAsync(Group group, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            if (group.RequestablePortId == null)
                throw new InvalidOperationException($"Group {group.Id} has no requestable port, so the gateway cannot report its state.");

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await GetTransitionAsync(group.RequestablePortId.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (attempt < TransitionAttempts && IsWorthRepeating(e))
                {
                    _logger?.LogWarning($"Requesting the state of group {group.Id} failed (attempt {attempt}): {e.Message}");
                }

                await Task.Delay(TransitionRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Triggers a port (the equivalent of pressing its button). Returns the transition the gateway
        /// answers with, or null when it only acknowledges the command.
        /// Throws <see cref="GatewayException"/> with <see cref="Error.AdapterBusy"/> while the drive is busy.
        /// </summary>
        public async Task<Transition> SetStateAsync(int portId, int state = 0xff, CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.SetState, PayloadFactory.SetState(portId, state), cancellationToken, Command.GetTransition).ConfigureAwait(false);

            if (response.Command.Code == Command.GetTransition.Code)
                return ParseTransition(response);

            return null;
        }

        public Task<Transition> SetStateAsync(Port port, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (port == null)
                throw new ArgumentNullException(nameof(port));

            return SetStateAsync(port.Id, 0xff, cancellationToken);
        }

        // A denied request has already been repeated once after logging in again.
        private static bool IsWorthRepeating(Exception e)
        {
            if (e is GatewayException gatewayException)
                return gatewayException.Error != Error.PermissionDenied;

            return e is GatewayProtocolException || e is TimeoutException;
        }

        private static Transition ParseTransition(Message response)
        {
            byte[] data = response.Payload?.ToByteArray() ?? new byte[0];

            try
            {
                return new Transition(data);
            }
            catch (InvalidPackageLengthException e)
            {
                throw new GatewayProtocolException($"The gateway returned a transition of {data.Length} bytes, which is too short.", e);
            }
        }
    }
}

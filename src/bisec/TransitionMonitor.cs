using BiSec.Library.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    /// <summary>
    /// Keeps a group's state current while its drive is moving, using the legacy app's refresh policy:
    /// ask again after the reported drive time, and every five seconds while auto-close is pending.
    /// </summary>
    public class TransitionMonitor
    {
        // DeviceActionsScreenCtrl.RELOAD_AUTO_CLOSE_DELAY
        public static readonly TimeSpan AutoCloseRefreshDelay = TimeSpan.FromSeconds(5);

        // DeviceActionsScreenCtrl.RELOAD_STATE_DELAY
        public static readonly TimeSpan MissingTransitionRefreshDelay = TimeSpan.FromSeconds(5);

        private readonly GatewayClient _client;

        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

        public TransitionMonitor(GatewayClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// How long to wait before asking for the state again, or null when the state is settled.
        /// </summary>
        public static TimeSpan? GetRefreshDelay(Transition transition)
        {
            if (transition == null)
                return MissingTransitionRefreshDelay;

            if (transition.AutoClose)
                return AutoCloseRefreshDelay;

            if (transition.Error || transition.DriveTime == 0)
                return null;

            return TimeSpan.FromSeconds(transition.DriveTime);
        }

        /// <summary>
        /// Requests the group's state and keeps requesting it until it is settled.
        /// While auto-close is pending this runs until the drive closes; cancel to stop earlier.
        /// </summary>
        /// <param name="onTransition">Called with every state received, including the last one.</param>
        /// <returns>The settled state.</returns>
        public async Task<Transition> FollowAsync(Group group, Action<Transition> onTransition = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Transition transition = await RequestAsync(group, onTransition, cancellationToken).ConfigureAwait(false);

            return await FollowFromAsync(group, transition, onTransition, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Triggers a port of the group and follows the drive until its state is settled.
        /// </summary>
        public async Task<Transition> SetStateAndFollowAsync(Group group, Port port, Action<Transition> onTransition = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            // Checked before anything is sent: the drive must not start moving for a call that cannot follow it.
            if (group.RequestablePortId == null)
                throw new InvalidOperationException($"Group {group.Id} has no requestable port, so its drive cannot be followed. Use GatewayClient.SetStateAsync to trigger the port without following.");

            Transition transition = await _client.SetStateAsync(port, cancellationToken).ConfigureAwait(false);
            if (transition != null)
                onTransition?.Invoke(transition);

            return await FollowFromAsync(group, transition, onTransition, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Transition> FollowFromAsync(Group group, Transition transition, Action<Transition> onTransition, CancellationToken cancellationToken)
        {
            while (true)
            {
                TimeSpan? delay = GetRefreshDelay(transition);
                if (delay == null)
                    return transition;

                await Delay(delay.Value, cancellationToken).ConfigureAwait(false);
                transition = await RequestAsync(group, onTransition, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<Transition> RequestAsync(Group group, Action<Transition> onTransition, CancellationToken cancellationToken)
        {
            Transition transition = await _client.GetTransitionAsync(group, cancellationToken).ConfigureAwait(false);
            onTransition?.Invoke(transition);

            return transition;
        }
    }
}

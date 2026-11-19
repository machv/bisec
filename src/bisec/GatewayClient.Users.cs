using BiSec.Library.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public partial class GatewayClient
    {
        public async Task<User[]> GetUsersAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.Jmcp, PayloadFactory.GetUsers(), cancellationToken).ConfigureAwait(false);

            return ParseJsonResponse<User[]>(response.Payload.TextContent) ?? new User[0];
        }

        /// <summary>
        /// Creates a user and returns its id.
        /// </summary>
        public async Task<int> AddUserAsync(string userName, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.AddUser, PayloadFactory.AddUser(userName, password), cancellationToken).ConfigureAwait(false);

            return RequirePayload(response, Command.AddUser, 1)[0];
        }

        public async Task RemoveUserAsync(int userId, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.RemoveUser, PayloadFactory.RemoveUser(userId), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the ids of the groups the user may operate.
        /// </summary>
        public async Task<int[]> GetUserRightsAsync(int userId, CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.GetUserRights, PayloadFactory.GetUserRights(userId), cancellationToken).ConfigureAwait(false);

            // GET_USER_RIGHTS response payload: [userId][groupId...]
            byte[] data = RequirePayload(response, Command.GetUserRights, 1);
            int[] groupIds = new int[data.Length - 1];
            for (int i = 1; i < data.Length; i++)
                groupIds[i - 1] = data[i];

            return groupIds;
        }

        /// <summary>
        /// Replaces the set of groups the user may operate.
        /// </summary>
        public async Task SetUserRightsAsync(int userId, IEnumerable<int> groupIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.SetUserRights, PayloadFactory.SetUserRights(userId, groupIds), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Renames a user. Users may rename themselves; the administrator may rename anyone
        /// (firmware EE001425-10 or newer).
        /// </summary>
        public async Task ChangeUserNameAsync(int userId, string userName, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireLogin();

            if (userId == _userId)
            {
                await RequestAsync(Command.ChangeUserName, PayloadFactory.ChangeUserName(userName), cancellationToken).ConfigureAwait(false);
                _userName = userName;
            }
            else
            {
                await RequireEditingOtherUsersAsync(cancellationToken).ConfigureAwait(false);
                await RequestAsync(Command.ChangeUserNameOfUser, PayloadFactory.ChangeUserNameOfUser(userId, userName), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Changes a user's password. Users may change their own; the administrator may change
        /// anyone's (firmware EE001425-10 or newer).
        /// </summary>
        public async Task ChangePasswordAsync(int userId, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireLogin();

            if (userId == _userId)
            {
                await RequestAsync(Command.ChangePassword, PayloadFactory.ChangePassword(password), cancellationToken).ConfigureAwait(false);
                _password = password;
            }
            else
            {
                await RequireEditingOtherUsersAsync(cancellationToken).ConfigureAwait(false);
                await RequestAsync(Command.ChangePasswordOfUser, PayloadFactory.ChangePasswordOfUser(userId, password), cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RequireEditingOtherUsersAsync(CancellationToken cancellationToken)
        {
            if (!IsAdmin)
                throw new UnauthorizedAccessException("Only the administrator can change another user's name or password.");

            if (!EnforceFirmwareFeatureGates)
                return;

            GatewayVersion version = await GetFirmwareVersionAsync(false, cancellationToken).ConfigureAwait(false);
            if (version.Number < GatewayVersion.NumberEe001425_10)
                throw new NotSupportedException($"Gateway firmware {version.Name} cannot edit other users; EE001425-10 or newer is needed.");
        }
    }
}

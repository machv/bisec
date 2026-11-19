# BiSec.Library

[![NuGet Version](http://img.shields.io/nuget/v/bisec.svg?style=flat)](https://www.nuget.org/packages/bisec/)

This repository contains unofficial SDK for communicating with Hörmann BiSecur Gateway.

There are no guarantees that this would work on your device, please use with caution.

## Usage

```csharp
using BiSec.Library;
using BiSec.Library.Models;

// Find gateways on the local network (listens for 3 seconds).
var gateways = await new GatewayDiscovery().DiscoverAsync();
var gateway = gateways[0];

using var client = new GatewayClient(gateway);
LoginResult login = await client.LoginAsync("admin", "0000");

foreach (Group group in await client.GetGroupsAsync())
{
    Console.WriteLine(group);

    if (group.RequestablePortId == null)
        continue; // this group cannot report its state

    Transition transition = await client.GetTransitionAsync(group);
    ActorStateInfo state = ActorStateInterpreter.Interpret(transition, group.Type);
    Console.WriteLine($"{state.State}, driving: {state.IsDriving}, message: {state.Message}");
}

await client.LogoutAsync();
```

Pressing a button and following the drive until it stops:

```csharp
Group garage = (await client.GetGroupsAsync())[0];
Port impulse = garage.Ports.First(p => p.Type == PortType.Impuls);

var monitor = new TransitionMonitor(client);
Transition settled = await monitor.SetStateAndFollowAsync(garage, impulse,
    t => Console.WriteLine($"{t.ActualState / 2.0} %"));
```

What the client covers:

| Area | Calls |
| --- | --- |
| Session | `LoginAsync`, `LogoutAsync`, `PingAsync`, automatic re-login on `PERMISSION_DENIED` |
| Gateway | `GetNameAsync`, `SetNameAsync`, `GetGatewayVersionAsync`, `GetFirmwareVersionAsync` |
| Users | `GetUsersAsync`, `AddUserAsync`, `RemoveUserAsync`, `GetUserRightsAsync`, `SetUserRightsAsync`, `ChangeUserNameAsync`, `ChangePasswordAsync` |
| Groups | `GetGroupsAsync`, `GetGroupsForUserAsync`, `CreateGroupAsync`, `RenameGroupAsync`, `SetGroupTypeAsync`, `SetRequestablePortAsync`, `SetGroupedPortsAsync`, `DeleteGroupAsync` |
| Ports | `GetPortCountAsync`, `AddPortAsync`, `LearnPortAsync`, `AddPortToGroupAsync`, `SetPortTypeAsync`, `RemovePortAsync` |
| Key-value store | `GetValuesAsync`, `SetValueAsync` |
| State | `GetTransitionAsync`, `SetStateAsync`, `TransitionMonitor`, `ActorStateInterpreter` |
| WiFi | `GetWifiStateAsync`, `ScanWifiAsync`, `GatewayWebConfiguration` (credentials, over the gateway's clear-text web page) |
| Anything else | `SendRawAsync` |

Errors:

- `GatewayException` - the gateway answered with an error; `Error` holds the code.
- `GatewayProtocolException` - the answer was not what the protocol allows.
- `TimeoutException` - no answer in time. `IOException` - the connection dropped.

Remote access through the Hörmann portal is not implemented.

### Upgrading from 0.0.x

- Failures throw instead of returning `false` or `null`; `LoginAsync` returns a `LoginResult`.
- `SetStateAsync` returns the `Transition` the gateway answers with.
- `GetGroupTypesAsync` is gone. Use `GetValuesAsync` (the raw key-value store) or the `Type` and
  `RequestablePortId` that `GetGroupsAsync` fills in.
- `GetGroupsAsync(bool forUser)` became `GetGroupsAsync()`, which asks for the logged-in user's
  groups by itself, and `GetGroupsForUserAsync(userId)`.
- `Command` members are all PascalCase (`Command.GetWifiState`, not `Command.GET_WIFI_STATE`).
- `GatewayDiscovery.Discover()` returns null when no gateway answers within the timeout;
  `DiscoverAsync()` returns all of them.
- Request timeouts follow the app: 15 s by default instead of 5 s (`GatewayConnection.Timeouts`).
- Connecting is limited to 20 s (`GatewayConnection.ConnectTimeout`). Within that time an
  attempt that fails because the gateway is unreachable, or that gets no answer for 1.5 s, is
  replaced by a new one; a real gateway was at times slow to accept a connection, several
  seconds and once more than ten.

## Trying it against a real gateway

`src/harness` is a small console program for manual checks. Read-only commands run as they are;
a command that changes the gateway needs `--write` and a typed confirmation. See
[src/harness/README.md](src/harness/README.md).

## Protocol

[docs/PROTOCOL.md](docs/PROTOCOL.md) describes the wire format, every command and what is still
unknown. [wireshark/bisecur.lua](wireshark/bisecur.lua) is a Wireshark dissector for TCP port 4000.

## Resources

This project is hugely inspired by a great `bisdk` library https://github.com/bisdk/sdk.

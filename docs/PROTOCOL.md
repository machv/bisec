# BiSecur gateway protocol

What this library knows about the protocol between a client and a Hörmann BiSecur gateway.

**Source.** Everything here was read from the legacy BiSecur app (v1.4.5), an Adobe AIR
application whose logic is ActionScript bytecode in `assets/Bisecur.swf`. `tools/abcdump.py`
turns that file into readable pseudo-source. Where the app never reads an answer, its layout is
unknown; those places are marked **unverified** and collected at the end.

Class names in parentheses refer to the app.

## Transport

| Purpose | Transport | Port |
| --- | --- | --- |
| Discovery request | UDP broadcast | 4001 |
| Discovery answers | UDP | 4002 |
| Commands | TCP | 4000 |
| WiFi credentials | HTTP (clear text), `/index.htm` | 80 |

### Discovery (`GatewayBroadcast`, `Broadcast`)

The client binds UDP port 4002 and broadcasts the ASCII text `<Discover target="LogicBox" />`
to port 4001. Each gateway answers with one XML element carrying `mac` and `protocol`
(this library also reads `hwVersion` and `swVersion`). The app listens for 3 seconds and keeps
one entry per MAC. The gateway's name is not in the answer; the app reads it afterwards with
`GET_NAME`, which needs no login.

### Framing (`TMCPPackage`, `MCPPackage`)

Every package travels as **uppercase ASCII hex text**. Decoded, it is:

```
sender(6)  receiver(6)  | length(2, big endian)  tag(1)  token(4)  command(1)  payload(n)  checksum(1) |  checksum(1)
                         '----------------------------- message ------------------------------------'
```

- **Addresses.** The gateway's address is its MAC. A client starts with `000000000000`; the
  gateway addresses its answers to a client id it assigns, and the client uses that id from
  then on.
- **Length** counts the message from the length field through the message checksum
  (9 + payload bytes).
- **Tag** pairs an answer with its request (0..127). Packages the gateway sends on its own
  carry tag `0xFF`.
- **Token** is `00000000` before login and the session token afterwards.
- **Command** has bit 7 set in an answer.
- **Message checksum** is the low byte of the sum of all message bytes before it.
- **Package checksum** is the low byte of the sum of the *ASCII character codes* of the hex
  text before it.

Neither the app nor this library verifies checksums on receipt.

To read a package from the stream: read 28 characters (two addresses and the length), then
`(length - 2 + 1) * 2` more.

### Session (`MCPPackageQueue`)

- One request is outstanding at a time.
- An incoming package is accepted when its token equals the session token or is zero.
- The answer to `LOGIN` carries the new token; any `LOGOUT` package clears it. A `LOGOUT` with
  tag `0xFF` means the gateway ended the session.
- On `PERMISSION_DENIED` the app logs in again with the stored credentials.
- A package that has started arriving and then stalls for 500 ms is given up on
  (`MCPReaderExtension`). The app discards what it has; this library closes the connection,
  because the position of the next package can no longer be known.
- Answer timeouts (`Timings`): `GET_NAME` 4 s; `LOGIN`, `CHANGE_PASSWD`, `SCAN_WIFI` 10 s;
  `ADD_PORT`, `INHERIT_PORT` 25 s; everything else 15 s. Connecting times out after 1.5 s.

## Commands (`MCPCommands`, `MCPBuilder`)

"Sent" tells whether the app ever sends the command. Payload bytes are listed in order;
`text` is UTF-8 without terminator.

| Code | Name | Sent | Request payload | Answer payload |
| ---: | --- | :-: | --- | --- |
| 0 | PING | no | - | - |
| 1 | ERROR | answer | | `[errorCode]` |
| 2 | GET_MAC | no | - | **unverified** |
| 3 | SET_VALUE | yes | `[key][value]` | not read |
| 4 | GET_VALUE | no | `[key]` | **unverified** |
| 5 | DEBUG | no | unknown | unknown |
| 6 | JMCP | yes | JSON text | JSON text |
| 7 | GET_GW_VERSION | yes | - | text, e.g. `EE001425-10` |
| 16 | LOGIN | yes | `[nameByteLength][name][password]` | `[userId][token:4]` |
| 17 | LOGOUT | yes | - | not read |
| 32 | GET_USER_IDS | no | unknown | unknown |
| 33 | GET_USER_NAME | no | unknown | unknown |
| 34 | ADD_USER | yes | `[nameByteLength][name][password]` | `[userId]` |
| 35 | CHANGE_PASSWD | yes | `[password]` | not read |
| 36 | REMOVE_USER | yes | `[userId]` | not read |
| 37 | SET_USER_RIGHTS | yes | `[userId][groupId...]` | not read |
| 38 | GET_NAME | yes | - | text |
| 39 | SET_NAME | yes | `[name]` | not read |
| 40 | GET_USER_RIGHTS | yes | `[userId]` | `[userId][groupId...]` |
| 41 | ADD_PORT | yes | - | `[portId]` |
| 42 | ADD_GROUP | yes | - | `[groupId]` |
| 43 | REMOVE_GROUP | yes | `[groupId]` | not read |
| 44 | SET_GROUP_NAME | yes | `[groupId][name]` | not read |
| 45 | GET_GROUP_NAME | no | unknown | unknown |
| 46 | SET_GROUPED_PORTS | yes | `[groupId][portId...]` | not read |
| 47 | GET_GROUPED_PORTS | no | unknown | unknown |
| 48 | GET_PORTS | yes | - | one byte per port (the app only counts them) |
| 49 | GET_TYPE | no | `[portId]` | **unverified** |
| 50 | GET_STATE | no | unknown | unknown |
| 51 | SET_STATE | yes | `[portId][state]`, state `0xFF` = trigger | answered with `HM_GET_TRANSITION` |
| 52 | GET_PORT_NAME | no | unknown | unknown |
| 53 | SET_PORT_NAME | no | unknown | unknown |
| 54 | SET_TYPE | yes | `[portId][portType]` | not read |
| 64 | GET_GROUP_IDS | no | unknown | unknown |
| 65 | INHERIT_PORT | yes | - | `[portId]` |
| 66 | REMOVE_PORT | yes | `[portId]` | `[portId]` |
| 67 | CHANGE_USER_NAME | yes | `[name]` | not read |
| 68 | CHANGE_USER_NAME_OF_USER | yes | `[userId][name]` | not read |
| 69 | CHANGE_PASSWORD_OF_USER | yes | `[userId][password]` | not read |
| 80 | SET_SSL | no | unknown | unknown |
| 81 | SCAN_WIFI | yes | - | answered after the `WIFI_FOUND` packages |
| 82 | WIFI_FOUND | pushed | | `[byte][ssid]`, first byte **unverified** |
| 83 | GET_WIFI_STATE | yes | - | `[wifiState]` |
| 112 | HM_GET_TRANSITION | yes | `[portId]` | transition, see below |

"not read" means the app only checks that the answer is not an error.

### JMCP requests

| Request | Answer |
| --- | --- |
| `{"cmd":"GET_USERS"}` | `[{"id":0,"name":"admin","isAdmin":true,"groups":[0,1]}, ...]` |
| `{"cmd":"GET_GROUPS"}` | `[{"id":0,"name":"Garage","ports":[{"id":0,"type":1}]}, ...]` |
| `{"cmd":"GET_GROUPS", "forUser":3}` | the same, limited to that user's groups |
| `{"cmd":"GET_VALUES"}` | `{"00":1,"01":5, ... ,"63":0}` |

The app asks for all groups as administrator and for `forUser` with its own id otherwise, and
only reads the user list as administrator.

### Error codes (`MCPErrors`)

`0` COMMAND_NOT_FOUND, `1` INVALID_PROTOCOL, `2` LOGIN_FAILED, `3` INVALID_TOKEN,
`4` USER_ALREADY_EXISTS, `5` NO_EMPTY_USER_SLOT, `6` INVALID_PASSWORD, `7` INVALID_USERNAME,
`8` USER_NOT_FOUND, `9` PORT_NOT_FOUND, `10` PORT_ERROR, `11` GATEWAY_BUSY,
`12` PERMISSION_DENIED, `13` NO_EMPTY_GROUP_SLOT, `14` GROUP_NOT_FOUND, `15` INVALID_PAYLOAD,
`16` OUT_OF_RANGE, `17` ADD_PORT_ERROR, `18` NO_EMPTY_PORT_SLOT, `19` ADAPTER_BUSY.

`COMMAND_NOT_FOUND` in answer to `GET_GW_VERSION` means firmware EE001425-06 or older.

## Data model

Limits used by the app: 16 ports, 64 values, 10 users. User id 0 is the administrator.

### Key-value store (`GatewayValues`)

| Key | Meaning |
| --- | --- |
| `groupId` (0..15) | group type |
| `groupId + 16` (16..31) | requestable port: the port whose state stands for the group; `255` = the group cannot report its state |
| 32..63 | not used by the app |

### Group types and their ports (`GroupTypes`, `PortTypes`)

| Group type | Allowed port types |
| --- | --- |
| 1 sectional door, 2 horizontal sectional door, 5 sliding gate | impulse, up, down, half, light |
| 3 swing gate single | impulse, half, up, down, light |
| 4 swing gate double | impulse, walk, up, down, light |
| 6 door | auto close, impulse, light, on/off |
| 7 light, 8 other, 9 jack | on, off, on/off, impulse |
| 10 SmartKey | lock, unlock, open door |
| 11 to 14 Pilomat (bollard, road blocker, lifting bar, tyre killer) | lift, sink |
| 15 barrier | impulse, up, down |

Port types: `1` impulse, `2` auto close, `3` on/off, `4` up, `5` down, `6` half, `7` walk,
`8` light, `9` on, `10` off, `11` lock, `12` unlock, `13` open door, `14` lift, `15` sink.

### WiFi state (`WifiStates`)

`0` connected, `1` not connected, `64` busy, `128` AP not found, `129` security mismatch,
`130` authentication failure, `131` connection failure. Bit 7 marks a failure, bit 6 an
attempt in progress.

## Transition (`Parsing.parseHmTransition`)

The state of a drive, answered to `HM_GET_TRANSITION` and to `SET_STATE`.

| Bytes | Field | Notes |
| --- | --- | --- |
| 1 | actual | 0..200, i.e. half percent steps; 0 closed, 200 open |
| 1 | desired | same scale |
| 2 | flags and drive time | bit 7 of the first byte = error, bit 6 = auto close; when either is set the top nibble is cleared; the remaining 12 bits are the drive time in seconds (not read when error is set) |
| 2 | gk | drive class |
| 2 | HCP status | only when the high byte of gk is below 252 |
| 8 | exst | drive-specific status; the app stores it reversed |

HCP bits, first byte: `1` position open, `2` position closed, `4` option relay,
`8` light barrier, `16` error, `32` driving to close, `64` driving, `128` half opened.
Second byte: `1` forecast lead time, `2` learned, `4` not referenced.

After parsing the app adjusts the drive time: an HCP that is not driving but reports a drive
time or a forecast lead time, and an HCP that is driving with drive time 0, both get drive
time 2; an ESE drive that is driving gets 5.

### Drive classes (`ActorClasses`)

| gk | Drive | Interpretation beyond the default |
| --- | --- | --- |
| `0x0902` | ESE | half open is `actual == 100` |
| `0x0905` | HET-S | - |
| `0x0908` | HET-E1 | lock state from `exst[5]` bits 0 and 1, permanent unlock from `exst[7]` bit 1 |
| `0x0909` | HET-E2 | open / closed / in between, HCP ignored |
| `0x090D` | HET-E2-SL | retracted / extended / not retracted from two pins in `exst[5]` |
| `0x0701` | PortaMatic | configuration in `exst[6]`, inputs in `exst[5]`: bolt locked / unlocked / error, or stop active |
| `0x0401`, `0x0402` | FES1, FES1-1 | - |
| `0x0604` | SmartKey | lock state in `exst[7]`, battery in `exst[5]` |
| `0x011B`, `0x011F` | VersaMatic double | one position per leaf from `exst[5]` and `exst[4]` |
| `0x011A`, `0x011E` | VersaMatic single | - |
| `>= 0xFC00` | undefined | default handling |

Indices refer to the reversed `exst`.

### Refresh policy (`TransitionCollector`, `DeviceActionsScreenCtrl`)

- A transition with a drive time, no error and no auto close is requested again after that
  many seconds.
- While auto close is set, the app asks again every 5 seconds.
- A failed request is repeated; the third failure in a row is reported.
- After `SET_STATE` the app uses the transition in the answer and applies the same rules.

## Workflows

- **Create a group:** `ADD_GROUP`, then `SET_GROUP_NAME`; the type with `SET_VALUE(groupId, type)`
  and the requestable port with `SET_VALUE(groupId + 16, portId)`.
- **Add a channel to a group:** `ADD_PORT` (copy from a hand transmitter) or `INHERIT_PORT`
  (learn from the drive; not offered for SmartKey groups), then `SET_TYPE(portId, type)`, then
  `SET_GROUPED_PORTS(groupId, all port ids)`. The app refuses when 16 ports exist.
- **Delete a group:** `REMOVE_PORT` for each of its ports (`PORT_NOT_FOUND` counts as done),
  then `REMOVE_GROUP`.
- **Rename or re-password a user:** `CHANGE_USER_NAME` / `CHANGE_PASSWD` for oneself; the
  administrator uses `CHANGE_USER_NAME_OF_USER` / `CHANGE_PASSWORD_OF_USER` for others, which
  the app offers from firmware EE001425-10 on.
- **Scan for WiFi:** `SCAN_WIFI`; the networks arrive as `WIFI_FOUND` packages before the
  answer. `GATEWAY_BUSY` means a scan is already running.
- **Set WiFi credentials:** not part of this protocol. The app reads the current values from
  `http://<gateway>/index.htm` and posts the form fields `Ssid`, `wsec=WPAAuto` and `pw` to
  the same address, unencrypted.

Input rules the app applies (`StringValidator`): user and gateway names 2 to 20 characters,
group names 1 to 20. Passwords should have 10 to 20 characters with upper case, lower case,
digit and special character; the app warns about weaker ones but accepts them.

## Remote access through the portal (not implemented)

The app can reach a gateway through Hörmann's portal. This library does not implement it.

- The same hex text is posted over HTTPS as form field `mcp` to
  `sslbisecapp.itbcloud.de/m2mcloud/gw_command`, with HTTP Basic authentication (device id and
  password); the answer body is one or more packages.
- `.../m2mcloud/gw_onlinestatus` and `.../m2mcloud/gw_list` take the form fields `id` and `cmd`
  and return JSON (`Gatewaystatus`: MAC to `online`).

## Not settled by the app

These need a capture or a check against a real gateway (`bisec-harness probe` prints the raw
answers of the first two):

- the answer layouts of `GET_MAC`, `GET_VALUE` and `GET_TYPE`;
- whether the bytes answered to `GET_PORTS` are port ids;
- the meaning of the first byte of `WIFI_FOUND`, and which tag those packages carry;
- the payloads of the commands the app never sends;
- the HTML of the gateway's WiFi page.

## Confirmed on a real gateway

Observed with `bisec-harness` on firmware EE001425-14:

- `PING`, `GET_NAME` and `GET_GW_VERSION` are answered before login.
- The gateway assigns the client id in its first answer (`000000000006` was seen), before login.
- `LOGIN` answers `[userId][token:4]`; `LOGOUT` is answered with a `LOGOUT` package.
- `GET_GROUPS` with `forUser`, `GET_VALUES` and `GET_PORTS` answer as described above; names
  are UTF-8.
- `ADD_USER` answers `[userId]`, and the new user can log in at once with no group rights.
  The gateway accepted it, as well as `GET_USERS` and `GET_USER_RIGHTS`, from a user that is
  not the administrator; the app only offers these to user 0. It also accepted a 9-character
  password, which the app would flag as too short.
- Now and then the gateway was unreachable for about three seconds at first contact: one TCP
  connect took 3 s from Windows and one failed with "no route to host" from Linux, while the
  attempts right after took 3 ms. It happened three times early in a session and not in eight
  later attempts; the cause is not known.
- A unicast discovery request sent from inside WSL (behind a NAT) got no answer. Broadcast
  discovery on the gateway's own network was not tried.

# bisec-harness

A console program for checking the library against a real gateway by hand. It is not part of
the NuGet package.

Run it from `bisec/src`:

```
dotnet run --project harness/bisec-harness.csproj -- <command> [arguments] [options]
dotnet run --project harness/bisec-harness.csproj -- help
```

## Safety

- **Read-only by default.** A command that changes the gateway is refused unless `--write` is
  given, and then asks you to type `yes` after showing what it is about to do. `--yes` skips
  the question.
- **Nothing is stored.** The gateway address and credentials come from options, from
  `BISECUR_HOST`, `BISECUR_MAC`, `BISECUR_USER`, `BISECUR_PASSWORD`, or from a prompt. A
  password passed with `--password` is visible to other processes on the machine; the hidden
  prompt is not.
- `--trace` prints every package, including the login package, which contains the password.
- `press` and `press-watch` **move the drive**. Only use them with the door or gate in view.
- `wifi-config-read` and `wifi-config-write` talk to the gateway's web page over unencrypted
  HTTP; the WiFi passphrase travels in clear text. That is how the original app does it.
  `wifi-config-write` can take the gateway off your network if the credentials are wrong.

## A first session

```
dotnet run --project harness/bisec-harness.csproj -- discover
export BISECUR_HOST=192.168.1.50 BISECUR_MAC=5410EC000000 BISECUR_USER=admin

... -- info          # no login: ping, name, firmware
... -- groups        # groups, types, ports, requestable ports
... -- state         # state of every group, raw and interpreted
... -- users
... -- values
... -- wifi
```

## What to check

Confirmed so far on a real gateway (firmware EE001425-14): `info`, `groups`, `users` and
`add-user`, that is connecting, ping, name, firmware version, login, the group list with types
and requestable ports, the port count, the user list with rights, creating a user and logout.
Everything else below has only run against the in-process fake used by the automated tests.

**Basics**

1. `discover` finds the gateway and shows its name. (Run it from a machine on the gateway's own
   network; from inside WSL, which sits behind a NAT, a discovery request got no answer.)
2. `info` works without login. *Confirmed.*
3. `groups` shows the right type for each group and a sensible requestable port. *Confirmed.*
4. `state` matches what the door is actually doing. Compare the `=>` line with reality,
   especially for drives that are not plain garage doors.
5. `watch <groupId>` while someone operates the door with a hand transmitter.

**Open questions about the protocol** (the original app never reads these answers)

6. `probe --user <name>` prints the raw answers of `GET_MAC`, `GET_GW_VERSION` before login,
   `GET_VALUE`, `GET_PORTS` and `GET_TYPE`. The output settles their layouts.
7. At the end of every logged-in command the harness reports whether the gateway answered
   `LOGOUT`. *Confirmed: it does.*
8. `wifi-scan --trace` shows the `WIFI_FOUND` packages: their tag and their first payload byte.
9. `wifi-config-read` shows whether the page parsing matches your firmware's web page.

**Commands that change the gateway** (each needs `--write`)

10. `press-watch <groupId> <portId>` - does the drive move, and does the harness follow it
    until it stops?
11. User management on a throwaway user: `add-user`, `set-rights`, `rename-user`,
    `set-password`, `remove-user`.
12. Group and port management on a throwaway group: `create-group`, `add-port`,
    `set-requestable-port`, `delete-group`. `add-port` waits up to 25 seconds for the hand
    transmitter, or for the drive with `--learn`.
13. `set-name`, then set it back.

If something fails, run it again with `--trace` and keep the output (remove the login package
before sharing it).

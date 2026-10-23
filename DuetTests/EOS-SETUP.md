# Online rooms (Epic Online Services)

Duet Mode can connect people by room code instead of IP address. The host gets a 6-character code, and the joiner types it in. Epic Online Services (EOS) finds the other PC and connects the two directly, or through Epic's relay when routers get in the way. Nobody needs an Epic account or a port forward.

Epic's SDK and the product keys are never committed. Everything below is in `.gitignore`, and without it the code is left out of the build (`DUET_EOS` isn't defined), so Duet Mode only offers direct IP.

## Setup

Inside the `Moonscraper Chart Editor` folder (the Unity project):

| Put this | Here |
| --- | --- |
| The SDK's `SDK/Source` folder | `Assets/Plugins/EOS/Source` |
| `SDK/Bin/EOSSDK-Win64-Shipping.dll` | `EOS/EOSSDK-Win64-Shipping.dll` (beside `Assets`, not inside it) |
| The five portal values | `EOS/credentials.txt` |
| A file with the two lines below | `Assets/csc.rsp` |

`Assets/csc.rsp`:

```
-define:DUET_EOS
-define:EOS_DYNAMIC_BINDINGS
```

`EOS/credentials.txt` has one `Name=Value` per line, with values from the Epic Developer Portal (Product Settings, then SDK Download & Credentials):

```
ProductId=...
SandboxId=...
DeploymentId=...
ClientId=...
ClientSecret=...
```

The client uses the Peer2Peer policy and the Live sandbox.

## Why the DLL sits outside Assets

EOS can only start once each time its DLL is loaded, and the Unity editor never unloads native plugins. So `DuetEos` loads the DLL itself with `LoadLibrary`, hooks the SDK's bindings to it (`EOS_DYNAMIC_BINDINGS`), and frees it when the editor stops playing. Builds work the same way: **Build Processes > Windows x64 Distributable** copies the `EOS` folder next to the .exe.

## Testing

Both people must be on different PCs. Two copies on one PC sign in with the same device ID, so they look like the same person to EOS. On one PC, test with Direct IP and `127.0.0.1` instead.

`DuetTests/run.sh` checks splitting messages into P2P-sized packets and runs a whole session over packet links. It doesn't need the SDK.

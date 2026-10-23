# EOS spike

Checks that Epic Online Services runs in Unity 2018.4 before Duet Mode is built on it. Pressing F8 in Play mode starts the SDK, signs in with an anonymous device ID, creates a lobby under a room code, finds that lobby again by the code, and leaves it.

Epic's SDK and the product keys are never committed. Everything below is in `.gitignore`.

## Setup

Inside the `Moonscraper Chart Editor` folder (the Unity project):

| Put this | Here |
| --- | --- |
| The SDK's `SDK/Source` folder | `Assets/Plugins/EOS/Source` |
| `SDK/Bin/EOSSDK-Win64-Shipping.dll` | `EOS/EOSSDK-Win64-Shipping.dll` (beside `Assets`, not inside it) |
| A file containing `-define:DUET_EOS` | `Assets/csc.rsp` |
| The five portal values | `EOS/credentials.txt` |

`credentials.txt` has one `Name=Value` per line:

```
ProductId=...
SandboxId=...
DeploymentId=...
ClientId=...
ClientSecret=...
```

The DLL sits outside `Assets` on purpose. The editor never unloads a native plugin, and EOS can only start once per load, so the spike loads the DLL itself and frees it after each run.

## Run

Open the project, wait for it to compile, press Play, then press F8. A box at the top left lists each step and ends in `PASSED` or `FAILED at <step>`. The same lines go to the Console with the prefix `EOS spike:`.

To switch the spike off, delete `Assets/csc.rsp`.

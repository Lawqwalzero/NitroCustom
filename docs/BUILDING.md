# Building from source

## Requirements

— Windows 10 or 11, x64.

— [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

— [Inno Setup 6](https://jrsoftware.org/isdl.php) only if you want the installer: `winget install JRSoftware.InnoSetup`.

## Build and test

```powershell
git clone https://github.com/<owner>/NitroCustom.git
cd NitroCustom
dotnet build NitroCustom.slnx -c Release
dotnet test tests/NitroCustom.Core.Tests -c Release
```

The build must finish with 0 errors and 0 warnings, and every test must pass.

## Build the installer

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_installer.ps1 -Version 0.1.0-beta.1
```

The script publishes the app, service, agent and command-line tool as self-contained x64 builds into `publish\dist`, then compiles `installer\NitroCustom.iss`. The result is `installer\Output\NitroCustom-Setup-<version>.exe`.

Inno Setup prints one warning about `PrivilegesRequired=admin` together with a per-user registry entry. It is expected: the installer removes an old per-user autostart entry left by early builds.

## Repository layout

For how the parts work together and the safety rules, see [ARCHITECTURE.md](ARCHITECTURE.md).

| Path | Contents |
|---|---|
| `src/NitroCustom.Core` | Firmware access (WMI, EC and lighting HID), IPC protocol, shared logic |
| `src/NitroCustom.Service` | Windows service, the only process that touches hardware |
| `src/NitroCustom.App` | WPF tray app and window |
| `src/NitroCustom.Agent` | Light session helper: hotkeys and the on-screen notification |
| `src/NitroCustom.Cli` | `nitroctl` diagnostics tool |
| `tests/NitroCustom.Core.Tests` | Unit tests for the shared logic |
| `installer` | Inno Setup script |
| `tools` | Installer build script and icon generator |

## Safe development

— `nitroctl status` and `nitroctl light info` only read. Prefer them while testing on your own laptop.

— The service writes `C:\ProgramData\NitroCustom\settings.json`. Delete it to return to defaults.

— Do not run write commands from the tool on a laptop model that has not been verified.

— To test the other language, set the environment variable `NITROCUSTOM_LANGUAGE` to `en` or `ru` before starting the app. By default the language follows the Windows display language.

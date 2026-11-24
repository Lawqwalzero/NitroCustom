# Architecture

How NitroCustom is put together, where to change things, and which rules keep the firmware safe. Read this before changing code.

## Processes

| Process | Project | Runs as | Job |
|---|---|---|---|
| Service | `src/NitroCustom.Service` | Windows service, LocalSystem | The only owner of the hardware. Switches profiles, writes lighting, re-applies saved settings at boot, resume and power changes, polls the profile, serves the pipe API, keeps the agent running |
| App | `src/NitroCustom.App` | The user, no administrator rights | WPF window and tray icon. Talks to the service only |
| Agent | `src/NitroCustom.Agent` | The user, started by the service | Nitro key, mode key and the on-screen notification. Starts the app in the tray at logon |
| nitroctl | `src/NitroCustom.Cli` | Any | Diagnostics. Direct commands need administrator rights, `svc` commands go through the service |

`src/NitroCustom.Core` is the shared library: firmware access, the IPC protocol, language, model rules and the notification state. Tests live in `tests/NitroCustom.Core.Tests` and also reference the service project.

Why a service: the Acer WMI methods need administrator rights. The service avoids a UAC prompt at every logon and restores the saved mode before anyone signs in. The keys and the notification live in a user-session process because a service in session 0 never receives them.

## Talking to the hardware (Core, `Hardware/`)

| Path | What it is |
|---|---|
| WMI `root\WMI:AcerGamingFunction` | Profiles, sensors, fan state. Wrapped by `AcerWmi` behind `IAcerWmi`, command names and codes in `AcerProtocol` |
| EC over HID | The thermal mode is switched through the embedded controller the way Acer's own agent does. `EcHid` and `EcMode`. `NitroController` prefers it when the firmware reports 4 or 5 modes |
| Keyboard lighting over HID | The ENE controller `ACPI\ENEK5130`, report `A4`. `LightingHid`, `LightingProtocol`, `LightingController`, reconnecting wrapper `ReconnectingLightingDevice` |
| Firmware hotkey events | `HotkeyEventWatcher` (touchpad key) |
| Windows power mode | `WindowsPowerMode` follows the profile |

Everything that touches hardware sits behind an interface (`IAcerWmi`, `ILightingDevice`) so tests use fakes. Never call hardware code from a test.

## IPC (Core, `Ipc/`)

One JSON message per line over the named pipe `NitroCustom.v1`. Requests, responses and pushed events share `IpcMessage`.

| Commands | Events |
|---|---|
| `getState`, `getSensors`, `setProfile`, `cycleProfile`, `setLighting`, `setModeKeyColor`, `setUnverifiedConsent` | `profileChanged`, `lightingChanged`, `touchpad` |

`StateDto` carries the profile, sensors, lighting and the model status. New fields are optional with a default so older peers keep working. The pipe is open to authenticated local users only.

Settings are stored by the service in `C:\ProgramData\NitroCustom\settings.json` (`SettingsStore`). Only the service writes it.

## Safety: the model guard

NitroCustom was verified on one laptop model only. Writing to firmware is allowed without questions only on models listed in `SupportedModels.Verified` (`Core/Hardware/SupportedModels.cs`). The model comes from SMBIOS.

On any other model `NitroService` refuses every write: `SetProfile`, `CycleProfile`, `SetLighting`, `SetModeKeyColor`, the re-apply at boot, resume and power changes, and the mode-key colour writes. Reads still work. The window shows a notice with an Allow button, which calls `setUnverifiedConsent` and stores `AllowUnverifiedModel`. `nitroctl` blocks its direct write commands unless `--force` is given. An unreadable model counts as unverified.

Rules when changing code:

1. Every new write path in the service must go through `RequireWrites()` or check `WritesAllowed`.
2. Add a model to `SupportedModels.Verified` only after a hardware report that shows profiles, sensors and lighting working on real hardware, and update `docs/SUPPORTED_MODELS.md`.
3. `tests/NitroCustom.Core.Tests/ModelGuardServiceTests.cs` proves that a blocked model produces zero writes. Keep it passing and extend it for new write paths.

## Language

`Loc` (`Core/Loc.cs`) picks Russian when the Windows display language is Russian, English otherwise. The environment variable `NITROCUSTOM_LANGUAGE` (`en` or `ru`) overrides it for testing. Every process decides once at start.

| Where | How |
|---|---|
| Code | `Loc.T("русский текст", "English text")` |
| XAML | `{local:Tr 'русский текст', 'English text'}` (`TrExtension`) |
| Profile names | `ProfileNames.Title`, as Acer NitroSense names them: Eco, Quiet, Balanced, Performance, Turbo |
| Installer | `[CustomMessages]` in `installer/NitroCustom.iss`, English first |

Add a new language by extending `Loc.T` into a lookup, not by copying strings around.

## On-screen notification

`OsdState` (Core) is pure logic: timing, which events show a card, placement and DPI. It is unit-tested. `ModeOsd` and `OsdNative` (Agent) draw it with a layered Win32 window and GDI+. Startup and resume events never show a card. Nothing is shown in full-screen games or presentations (`SHQueryUserNotificationState`).

## Where to change what

| Task | Start here |
|---|---|
| New lighting effect | `LightingEffect` and `LightingProtocol` (Core), `Texts.Name`, `Texts.Effects`, an `Icon<Effect>` in `Icons.xaml` |
| New profile or sensor | `AcerProtocol`, `NitroController`, `Texts`, `ProfileNames`, the icons |
| New window text | `MainWindow.xaml` with `{local:Tr ...}` or `Loc.T` in code |
| New service command | `IpcCommands`, `PipeServer.Dispatch`, `NitroService` (with `RequireWrites()` if it writes) |
| New verified model | `SupportedModels.Verified`, `docs/SUPPORTED_MODELS.md`, `CHANGELOG.md` |
| Installer behaviour | `installer/NitroCustom.iss`, built by `tools/build_installer.ps1` |

## Conventions

— Code comments are English.

— Windows only, .NET 10, x64. The service is the only process that may write to hardware.

— The build must stay at 0 errors and 0 warnings (`-warnaserror`), and all tests must pass.

— No network access anywhere in the code.

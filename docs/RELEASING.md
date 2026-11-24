# Releasing

How a maintainer cuts a release. Follow the steps in order.

## 1. Before every release

| Check | How |
|---|---|
| Build is clean | `dotnet build NitroCustom.slnx -c Release -warnaserror` gives 0 errors and 0 warnings |
| Tests pass | `dotnet test tests/NitroCustom.Core.Tests -c Release --no-build` |
| Changelog is written | Add a section to `CHANGELOG.md` |
| Known limits are honest | Update the limitations in `README.md` and `README.ru.md` |
| Models agree | `SupportedModels.Verified` in `src/NitroCustom.Core/Hardware/SupportedModels.cs` and the table in `docs/SUPPORTED_MODELS.md` list the same models |
| Screenshots are current | `docs/images` still looks like the program |
| No personal data | No names, e-mail addresses, user profile paths or serial numbers anywhere in the tree |

## 2. Build the installer

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_installer.ps1 -Version 0.1.0-beta.1
Get-FileHash .\installer\Output\NitroCustom-Setup-0.1.0-beta.1.exe -Algorithm SHA256
```

Write the hash into `SHA256SUMS.txt` next to the installer in the format `<hash> *NitroCustom-Setup-0.1.0-beta.1.exe` (UTF-8, LF).

## 3. Smoke test the installer

Test the exact file you will publish, not a debug build.

1. Install on a machine or VM that never had NitroCustom. The service `NitroCustom` runs and the tray icon appears.
2. On the verified laptop switch each profile from the window, the mode key and the tray, change keyboard lighting and reboot. The saved profile and lighting must come back.
3. Install the new build over the old one. `settings.json` must stay unchanged.
4. Uninstall from Windows Settings. The service and the files in `Program Files\NitroCustom` must be gone.
5. On a laptop that is not the verified model, the window must show the read-only notice, and nothing changes until you press Allow.
6. Check both languages: set the environment variable `NITROCUSTOM_LANGUAGE` to `en` and to `ru` before starting the app.

Optionally upload the installer to [VirusTotal](https://www.virustotal.com) and link the report. A few heuristic detections are common for unsigned installers that register a service.

## 4. Tag and publish

```powershell
git tag -a v0.1.0-beta.1 -m "0.1.0-beta.1"
git push origin v0.1.0-beta.1
```

On GitHub, Releases, Draft a new release:

1. Choose the tag, title `NitroCustom 0.1.0-beta.1`.
2. Tick **Set as a pre-release** while the version is a beta.
3. Attach the installer and `SHA256SUMS.txt`.
4. Use this text for the notes:

```markdown
## NitroCustom 0.1.0-beta.1

First public beta. **Verified only on Acer Nitro ANV16S-41, BIOS V1.12, Windows 11.**
On other models NitroCustom starts read-only and writes nothing to the firmware until you click Allow. Read docs/SUPPORTED_MODELS.md first.

Changes: see CHANGELOG.md.

SHA-256: `<hash>`

The installer is not code-signed, so SmartScreen may warn you.
```

With the GitHub CLI: `gh release create v0.1.0-beta.1 <files> --prerelease --notes-file notes.md`.

## 5. After the release

1. Answer the first reports quickly. They shape the project.
2. When a model is confirmed by a report with output, add its id to `SupportedModels.Verified` (the part of the model name after "Nitro ", as the BIOS reports it) and to `docs/SUPPORTED_MODELS.md` with the BIOS and Windows versions. Until then it stays read-only for everyone else.
3. There is no auto-update. Each release means a new tag, installer and checksum.
4. Optional later: code signing through the [SignPath Foundation](https://signpath.org/) (free for open source).

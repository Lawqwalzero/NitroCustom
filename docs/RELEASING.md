# Releasing

How to cut a public release. Follow the steps in order.

## 1. One-time setup

1. On GitHub create a **new empty** repository (no README, no license, no .gitignore). The public history starts clean from this tree.
2. In GitHub, Settings, Emails, enable **Keep my email addresses private** and **Block command line pushes that expose my email**.
3. Decide how you want to be credited in `LICENSE` (it currently names the GitHub handle).
4. Create labels for issues: `hardware-report`, `bug`, `model-request`.

## 2. Before every release

| Check | How |
|---|---|
| Build is clean | `dotnet build NitroCustom.slnx -c Release` gives 0 errors and 0 warnings |
| Tests pass | `dotnet test tests/NitroCustom.Core.Tests -c Release` |
| Changelog is written | Add a section to `CHANGELOG.md` |
| Known limits are honest | Update the limitations in `README.md` and `README.ru.md` |
| Supported models table is current | `docs/SUPPORTED_MODELS.md` and `SupportedModels.Verified` in `src/NitroCustom.Core/Hardware/SupportedModels.cs` agree |
| No personal data | Search the tree for your name, e-mail, paths like `C:\Users\`, serial numbers |

## 3. Build the installer

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_installer.ps1 -Version 0.1.0-beta.1
Get-FileHash .\installer\Output\NitroCustom-Setup-0.1.0-beta.1.exe -Algorithm SHA256
```

Save the hash line into `SHA256SUMS.txt` next to the installer in the format `<hash> *NitroCustom-Setup-0.1.0-beta.1.exe`.

## 4. Smoke test the installer

Test the exact file you will publish, not a debug build.

1. Install on a machine or VM that never had NitroCustom. Check that the service `NitroCustom` runs and the tray icon appears.
2. On the verified laptop: switch each profile from the window, from the mode key and from the tray, then change keyboard lighting and reboot. The saved profile and lighting must come back.
3. Install the new build over the old one. `settings.json` must stay unchanged.
4. Uninstall from Windows Settings. The service and the files in `Program Files\NitroCustom` must be gone.
   On a laptop that is not the verified model, also check that the window shows the read-only notice and that nothing changes until you press Allow.
5. Upload the installer to [VirusTotal](https://www.virustotal.com) and keep the report link for the release notes. A few heuristic detections are common for unsigned installers that register a service.

## 5. Publish the code

The clean branch is `public-release`. Commit with your GitHub noreply identity so your personal address never enters the history:

```powershell
git -c user.name="<your GitHub handle>" -c user.email="<id>+<handle>@users.noreply.github.com" commit -m "NitroCustom 0.1.0-beta.1"
git remote add public https://github.com/<owner>/NitroCustom.git
git push public public-release:main
```

Your numeric id is in `https://api.github.com/users/<handle>`. Push only this branch to the public remote. Never push `main` of the private repository there.

## 6. Tag and create the release

```powershell
git tag -a v0.1.0-beta.1 -m "0.1.0-beta.1"
git push public v0.1.0-beta.1
```

On GitHub, Releases, Draft a new release:

1. Choose the tag `v0.1.0-beta.1`, title `NitroCustom 0.1.0-beta.1`.
2. Tick **Set as a pre-release**.
3. Attach `NitroCustom-Setup-0.1.0-beta.1.exe` and `SHA256SUMS.txt`.
4. Use the notes template below.

```markdown
## NitroCustom 0.1.0-beta.1

First public beta. **Verified only on Acer Nitro ANV16S-41, BIOS V1.12, Windows 11.**
Other models are untested and the program writes to firmware. Read docs/SUPPORTED_MODELS.md first.

Changes: see CHANGELOG.md.

SHA-256: `<hash>`
VirusTotal: <link>

The installer is not code-signed, so SmartScreen may warn you.
```

If you use the GitHub CLI, the same can be done with `gh release create v0.1.0-beta.1 <files> --prerelease --notes-file notes.md`.

## 7. Announce

— Post once, honestly, as a beta. Say which model is verified and that more models need testers.

— Read each subreddit's self-promotion rules before posting. Mention [nitrous](https://github.com/jeremyaliparo/nitrous) as an alternative; the community notices if you do not.

— Ask for hardware reports, not just downloads.

## 8. After release

1. Answer the first reports quickly. They shape the project.
2. When a model is confirmed by a report with output, add its id to `SupportedModels.Verified` (the part of the model name after "Nitro ", as the BIOS reports it) and to `docs/SUPPORTED_MODELS.md` with the BIOS and Windows versions. Until then it stays read-only for everyone else.
3. There is no auto-update. Each release means a new tag, installer and checksum.
4. Optional later: code signing through the [SignPath Foundation](https://signpath.org/) (free for open source).

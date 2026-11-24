# Contributing

Thank you for helping. The most useful contribution is a **hardware report** from a model that is not verified yet.

## Hardware reports

1. Read [docs/SUPPORTED_MODELS.md](docs/SUPPORTED_MODELS.md).
2. Run the read-only checks listed there.
3. Open an issue with the **Hardware report** template and remove personal data such as serial numbers before posting.

A model becomes verified only when a report shows profiles, sensors and keyboard lighting working on real hardware. Until then it stays read-only for everyone else.

## Bugs

Open an issue with the model, BIOS version, Windows version, what you did and what happened. For a crash, add the Windows event log entry. Please do not paste raw firmware dumps you do not understand.

## Code

1. Fork and branch from `main`.
2. Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), especially the safety rules about the model guard.
3. Build and test:

```powershell
dotnet build NitroCustom.slnx -c Release -warnaserror
dotnet test tests/NitroCustom.Core.Tests -c Release --no-build
```

4. Keep changes small and focused. Add or update tests. New write paths in the service must go through the model guard and be covered by `ModelGuardServiceTests`.
5. Texts: every user-visible string needs a Russian and an English version (`Loc.T` or `{local:Tr ...}`).
6. Code comments are English. No new dependencies without a good reason: the program makes no network connections and ships self-contained.

## What will not be accepted

— Changes that write to the firmware on unverified models without the guard.

— Telemetry or any network access.

— Acer logos or artwork. Acer, Nitro and NitroSense are trademarks of their owners.

## License

By contributing you agree that your work is released under the [MIT license](LICENSE).

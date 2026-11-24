# Supported models

NitroCustom talks to the firmware directly, so a model that was not tested can behave unexpectedly: wrong profile codes, a keyboard that ignores lighting commands, or nothing at all. Please check your laptop here before installing, and report what you find.

## Status

| Model | BIOS | Windows | Status |
|---|---|---|---|
| Acer Nitro ANV16S-41 | V1.12 | 11 | Verified: profiles, sensors, lighting, Nitro key, mode key |
| Anything else | | | Unknown |

## What happens on a model that is not verified

NitroCustom reads the model name from the BIOS (SMBIOS). If it is not in the verified list:

1. The window shows a notice with the model name and the button **Allow**. Sensors and the current mode are still shown.
2. The service refuses to switch profiles, change lighting or the mode-key colour, and it does not re-apply saved settings at boot, resume or power changes. The Nitro mode key does nothing.
3. After you press **Allow** and confirm, writing is enabled and remembered in `settings.json` (`AllowUnverifiedModel`). Press **Disable** to turn it off again.
4. `nitroctl` refuses commands that write to the firmware (`profile <name>`, `cycle`, `ec <name>`, `kb zone|raw-set|sweep`, `light` except `info`) unless you add `--force`.

If the model cannot be read at all, it counts as not verified.

## Check your laptop without installing anything

Run these in **PowerShell as administrator**. They only read information. The Acer WMI class is hidden from normal users: without administrator rights the third command returns nothing even on a supported laptop.

```powershell
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model
Get-CimInstance Win32_BIOS | Select-Object SMBIOSBIOSVersion
Get-CimInstance -Namespace root\WMI -ClassName AcerGamingFunction -ErrorAction SilentlyContinue | Select-Object -First 1
Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -like 'ACPI\ENEK*' } | Select-Object InstanceId, Status
```

What the results mean:

| Result | Meaning |
|---|---|
| `AcerGamingFunction` returns an object | The firmware exposes the Acer gaming WMI interface used for profiles and sensors |
| An `ACPI\ENEK...` device is present | The keyboard lighting controller NitroCustom drives is there. Other controller families are not supported |
| Either is missing | NitroCustom will not work properly on this laptop |

## Read-only test with the command-line tool

After installing, from an administrator PowerShell in the install folder:

```powershell
.\nitroctl.exe status
.\nitroctl.exe light info
```

`status` shows the current profile, sensors and keyboard zones. Do not run commands that write (`profile`, `kb`, `light static` and so on) on an untested model unless you accept the risk. `nitroctl` blocks them on unverified models unless you add `--force`.

## Report your model

Open an issue with the **Hardware report** template and paste the output above. Remove anything personal first, such as serial numbers, before posting.

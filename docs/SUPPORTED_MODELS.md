# Supported models

NitroCustom talks to the firmware directly, so a model that was not tested can behave unexpectedly: wrong profile codes, a keyboard that ignores lighting commands, or nothing at all. Please check your laptop here before installing, and report what you find.

## Status

| Model | BIOS | Windows | Status |
|---|---|---|---|
| Acer Nitro ANV16S-41 | V1.12 | 11 | Verified: profiles, sensors, lighting, Nitro key, mode key |
| Anything else | | | Unknown |

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

`status` shows the current profile, sensors and keyboard zones. Do not run commands that write (`profile`, `kb`, `light static` and so on) on an untested model unless you accept the risk.

## Report your model

Open an issue with the **Hardware report** template and paste the output above. Remove anything personal first, such as serial numbers, before posting.

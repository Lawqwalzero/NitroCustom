# Third-party notices

## nitrous

The layout of the embedded-controller HID request (`src/NitroCustom.Core/Hardware/EcHid.cs`) and the keyboard lighting effect table (`src/NitroCustom.Core/Hardware/LightingProtocol.cs`) follow the MIT-licensed project [nitrous](https://github.com/jeremyaliparo/nitrous) and were then checked on real hardware.

```
MIT License

Copyright (c) 2026 Jeremy Aliparo

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Linux acer-wmi driver

Mode codes in `src/NitroCustom.Core/Hardware/AcerProtocol.cs` were cross-checked against the Linux `acer-wmi` driver (GPL) and then verified on real hardware.

## NuGet packages

The service and the library use `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.WindowsServices` and `System.Management`, all published by Microsoft under the MIT license. The installer ships a self-contained .NET runtime, which is MIT licensed as well.

## Build tools and fonts

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php), which is not redistributed. Bahnschrift and Segoe UI come with Windows and are not included in this repository or in the installer.

# Third-Party Notices

Dynamic Island for Windows original source code is licensed under the MIT License (see `LICENSE`).
This file lists every third-party component that is redistributed in source or binary form, with its
license and required notice text. The same content must appear in the installer and the About/Credits page.

Add an entry *before* a dependency is merged, and verify that its license allows redistribution under MIT.
Versions are pinned in `Directory.Packages.props`. Resolved transitive packages are listed in each `packages.lock.json`.

## Entry template

```text
### <Component name> <version>
- Source: <official URL>
- License: <SPDX identifier> (<license URL>)
- Used by: <project/assembly>
- Redistributed: yes/no (build-only tools need not be listed unless shipped)
- Notice:
  <verbatim copyright and license notice required by the license>
```

## Components (redistributed)

### Microsoft.Extensions.Hosting 10.0.13 and its Microsoft.Extensions.* dependencies (10.0.13)
- Source: https://github.com/dotnet/runtime
- License: MIT (https://licenses.nuget.org/MIT). Taken from the package nuspec, 2026-10-25.
- Used by:
  - DynamicIsland.App: Generic Host, dependency injection, configuration, logging
  - DynamicIsland.Infrastructure: `Microsoft.Extensions.Logging.Abstractions`, referenced directly for the file log sink
- Redistributed: yes
- Notice:

  ```text
  The MIT License (MIT)

  Copyright (c) .NET Foundation and Contributors

  All rights reserved.

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

### Microsoft.Data.Sqlite 10.0.13 and Microsoft.Data.Sqlite.Core 10.0.13
- Source: https://github.com/dotnet/efcore
- License: MIT (https://licenses.nuget.org/MIT). Taken from nuget.org, 2026-10-26.
- Used by: DynamicIsland.Infrastructure: notes and tasks database (ADR 0023)
- Redistributed: yes
- Notice: the MIT License text above, "Copyright (c) .NET Foundation and Contributors".

### SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core, SQLitePCLRaw.lib.e_sqlite3, SQLitePCLRaw.provider.e_sqlite3 2.1.13
- Source: https://github.com/ericsink/SQLitePCL.raw
- License: Apache-2.0 (https://licenses.nuget.org/Apache-2.0). Taken from nuget.org, 2026-10-26. The project ships no
  NOTICE file.
- Used by: Microsoft.Data.Sqlite (native SQLite loading)
- Redistributed: yes (managed assemblies and the native `e_sqlite3.dll`)
- Notice: Licensed under the Apache License, Version 2.0 (https://www.apache.org/licenses/LICENSE-2.0). The full
  license text must accompany binary distributions; the installer and portable ZIP include it (P06).

### .NET 10 runtime: Microsoft.NETCore.App and Microsoft.WindowsDesktop.App (WPF) 10.0.12
- Source: https://github.com/dotnet/runtime, https://github.com/dotnet/wpf
- License: MIT (https://licenses.nuget.org/MIT). Taken from the runtime pack nuspecs, 2026-10-27.
- Used by: the release app payload, which is self-contained so users need no .NET install and no admin rights (ADR 0030)
- Redistributed: yes (release packages only)
- Notice: "Copyright (c) .NET Foundation and Contributors", MIT License (text as for Microsoft.Extensions.Hosting
  above). The runtime's own third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
  and https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT.

### Windows SDK .NET projection: Microsoft.Windows.SDK.NET.dll (10.0.19041.57) and WinRT.Runtime.dll
- Source: Microsoft.Windows.SDK.NET.Ref (https://aka.ms/WinSDKProjectURL); WinRT.Runtime from C#/WinRT
  (https://github.com/microsoft/CsWinRT)
- License: Microsoft.Windows.SDK.NET.dll: Windows SDK license terms (https://aka.ms/WinSDKLicenseURL), which allow
  distributing the files on the SDK's REDIST list. WinRT.Runtime.dll: MIT (C#/WinRT).
- Used by: DynamicIsland.App and Platform.Windows: media sessions (ADR 0020). The .NET SDK copies both into every app
  that targets a `net*-windows10.0.*` framework.
- Redistributed: yes, unmodified. The Windows SDK REDIST list (https://learn.microsoft.com/en-us/legal/windows-sdk/redist,
  updated 2024-10-21, section "Microsoft.Windows.SDK.NET.Ref") names `lib/net8.0/Microsoft.Windows.SDK.NET.dll` and
  `lib/net8.0/WinRT.Runtime.dll`, which may be distributed unmodified as part of a program so that it can call WinRT
  APIs. The shipped file is byte-identical to that package copy (checked 2026-10-29, P07-01).
- Notice: Microsoft.Windows.SDK.NET.dll is © Microsoft Corporation and is **not** covered by this project's MIT
  License. It remains under the Windows SDK license terms. Recipients may not modify it, separate it from Dynamic Island
  and distribute it on its own, or use it on a non-Microsoft platform. Microsoft provides it without warranty.

### Inno Setup 6 (setup and uninstall runtime embedded in DynamicIsland-Setup-win-x64.exe)
- Source: https://jrsoftware.org/isinfo.php
- License: Inno Setup License (https://jrsoftware.org/files/is/license.txt): use for any purpose including commercial,
  redistribution allowed; binary redistributions must keep the copyright notice and web addresses in place (the
  Setup "About" box keeps them). Read 2026-10-27.
- Used by: `packaging/installer/DynamicIsland.iss` (built with the Inno Setup 6 compiler, ADR 0031)
- Redistributed: yes (inside the installer)
- Notice: "Copyright (C) 1997-2026 Jordan Russell. Portions Copyright (C) 2000-2026 Martijn Laan." https://jrsoftware.org/

### SQLite (native library `e_sqlite3`, bundled by SQLitePCLRaw.lib.e_sqlite3 2.1.13)
- Source: https://sqlite.org
- License: public domain (https://sqlite.org/copyright.html)
- Used by: SQLitePCLRaw
- Redistributed: yes
- Notice: none required ("The author disclaims copyright to this source code.").

## Original artwork (not third-party)

- `assets/DynamicIsland.ico`: the app icon. Original work by the Dynamic Island for Windows contributors (MIT), generated from simple shapes by `tools/New-AppIcon.ps1`. No external icon sets, fonts or images are used.

## Build/test-only components (not redistributed)

### xunit.v3 4.0.2
- Source: https://github.com/xunit/xunit
- License: Apache-2.0. Taken from the package nuspec, 2026-10-25.
- Used by: the test projects only. It is never shipped in release packages.

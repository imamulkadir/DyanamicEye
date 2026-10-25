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
- Used by: DynamicIsland.App (Generic Host, dependency injection, configuration, logging abstractions)
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

## Build/test-only components (not redistributed)

### xunit.v3 4.0.2
- Source: https://github.com/xunit/xunit
- License: Apache-2.0. Taken from the package nuspec, 2026-10-25.
- Used by: the test projects only. It is never shipped in release packages.

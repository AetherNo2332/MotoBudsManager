# Repository Guidelines

## Project Structure & Module Organization

`src/MotoBudsManager/` contains the packaged WinUI 3 app, Windows Bluetooth transports, views, view-models, localized `.resw` files, and package manifest. Protocol logic that does not depend on WinRT belongs in `src/MotoBudsManager.Core/`; keep PDU/SPP codecs, command dispatch, device models, and feature whitelists there. Unit tests live in `tests/MotoBudsManager.Tests/`. Generated output (`bin/`, `obj/`, `artifacts/`, screenshots, certificates, and MSIX files) must remain untracked.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore MotoBudsManager.sln
dotnet build MotoBudsManager.sln -p:Platform=x64
dotnet test tests/MotoBudsManager.Tests -p:Platform=x64
.\BuildAndRun.ps1 .\src\MotoBudsManager\MotoBudsManager.csproj
```

`BuildAndRun.ps1` builds, registers, and launches the packaged app through `winapp`; never launch its generated `.exe` directly. For hardware-free UI testing, set `$env:MOTOBUDS_TEST_MODE='1'` in a Debug session, launch the app, then run `.\ui-tests.ps1 -AppPid <PID>`.

## Coding Style & Naming Conventions

Use four-space indentation and nullable reference types. Follow .NET naming: PascalCase for types, methods, properties, XAML controls, and view-models; camelCase for locals; `_camelCase` for private fields. Name views `*Page.xaml`, view-models `*ViewModel.cs`, and services `*Service.cs`. Use CommunityToolkit MVVM partial properties and `x:Bind` with an explicit mode. Keep business and protocol behavior out of code-behind; dialogs, pickers, and view-only navigation may remain there.

## Testing Guidelines

Use xUnit. Name tests by behavior, such as `Feed_ZeroCrcFirmwareResponse_IsAccepted`. Cover exact captured byte vectors, fragmented streams, timeouts, sequence rollover, model whitelists, and write-then-readback behavior. Hardware tests must restore the initial ANC state and must never send experimental commands automatically.

## Commit & Pull Request Guidelines

The repository has no established history, so use concise imperative commits such as `Add secure RFCOMM transport`. Keep commits focused. Pull requests must summarize user-visible behavior, list automated and hardware verification, link issues, and include screenshots for UI changes. Clearly label functionality based only on static reverse engineering.

## Security & Privacy

Never commit Bluetooth addresses, PFX files, certificates, raw device logs, or package output. Mask addresses in diagnostics. The application must remain offline and telemetry-free unless a future change explicitly documents and reviews new data handling.

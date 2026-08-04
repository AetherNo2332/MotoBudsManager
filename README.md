# Moto Buds Manager

[简体中文](README.zh-CN.md) | English

An unofficial, offline WinUI 3 desktop application for managing Motorola earbuds already paired with Windows. The hardware-verified path targets Moto Buds `XT2443-1` over RFCOMM/SPP; other models and reverse-engineered controls are clearly marked experimental.

> **Codex-generated project:** This repository was generated and implemented with OpenAI Codex under user direction. AI-generated code can contain defects—review and test changes before using them with hardware.

## Features

- Discovers paired earbuds without performing in-app pairing.
- Shows model-matched product artwork, battery level, and per-component charging state.
- Provides hardware-tested ANC, spatial audio, Hi-Res, gaming, enhancement, in-ear detection, find-earbud, fit-test, and dual-connection controls for XT2443-1.
- Sends low-battery Windows notifications below 20% while suppressing alerts for charging components.
- Supports English, Simplified Chinese, light/dark themes, and a responsive 760 × 600 minimum window.
- Keeps diagnostics in memory, masks Bluetooth addresses, and performs no telemetry or network upload.

## Requirements

- Windows 10 version 2004 or later, x64
- .NET 10 SDK, Windows App SDK 2.3, and WinApp CLI
- Windows Developer Mode for local deployment

## Build and test

```powershell
dotnet build MotoBudsManager.sln -c Release -p:Platform=x64
dotnet test MotoBudsManager.sln -c Release -p:Platform=x64
.\BuildAndRun.ps1 .\src\MotoBudsManager\MotoBudsManager.csproj
```

Use the Debug-only fake device mode for UI tests:

```powershell
winapp run .\src\MotoBudsManager\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64 --args '--test-mode'
```

See [INSTALL.md](INSTALL.md) for sideloading and [AGENTS.md](AGENTS.md) for contribution guidance.

## Safety and legal notice

Experimental controls may change earbud settings. This project is not affiliated with or endorsed by Motorola or Lenovo. Motorola names and trademarks belong to their respective owners. The MIT license covers the project code; bundled third-party product artwork and trademarks are excluded as described in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

Code is released under the [MIT License](LICENSE).

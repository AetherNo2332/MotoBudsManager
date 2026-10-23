# Build and Install Moto Buds Manager

Moto Buds Manager targets x64 Windows 10 version 2004 or later. The project is configured as self-contained, so a packaged build does not require a separate Windows App SDK runtime installation.

## Build from source

Install the .NET 10 SDK and WinApp CLI, enable Windows Developer Mode, and run:

```powershell
.\BuildAndRun.ps1 .\src\MotoBudsManager\MotoBudsManager.csproj `
  -SkipRun /p:Configuration=Release /p:Platform=x64
```

## Create a local development package

Generate your own development certificate. Certificates, passwords, and package output must never be committed:

```powershell
New-Item -ItemType Directory -Force .\artifacts\package

winapp cert generate `
  --manifest .\src\MotoBudsManager\Package.appxmanifest `
  --output .\artifacts\package\MotoBudsManager.Development.pfx `
  --password '<choose-a-password>' `
  --export-cer --install

$release = '.\src\MotoBudsManager\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64'
winapp package $release `
  --manifest "$release\AppxManifest.xml" `
  --executable MotoBudsManager.exe `
  --cert .\artifacts\package\MotoBudsManager.Development.pfx `
  --cert-password '<choose-a-password>' `
  --output .\artifacts\package\MotoBudsManager.msix
```

Both `SelfContained` and `WindowsAppSDKSelfContained` are enabled in the project, so the build already contains the .NET and Windows App SDK runtimes. Package the generated manifest and output together; a separate WinApp CLI `--self-contained` runtime cache is not required for this .NET build.

Install the generated package:

```powershell
Add-AppxPackage .\artifacts\package\MotoBudsManager.msix
```

Pair the earbuds in **Settings > Bluetooth & devices** before opening Moto Buds Manager. Keep the PFX and its password private. Only the public `.cer` certificate may accompany a development-signed sideload release; recipients must explicitly trust its publisher. Do not use this development certificate as a production signing identity.

In Settings, **Start silently at sign-in** is off by default. When enabled, the app starts in the notification area without opening its window. Compatible earbuds connected to Windows are monitored automatically; connection and ANC checks run every two seconds. The first ANC read is a baseline, not a change notification. Manual disconnect pauses automatic reconnection until the next Windows Bluetooth reconnect.

Moto Buds Manager is unofficial and is not affiliated with or endorsed by Motorola or Lenovo.

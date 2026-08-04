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
  --manifest .\src\MotoBudsManager\Package.appxmanifest `
  --executable MotoBudsManager.exe `
  --cert .\artifacts\package\MotoBudsManager.Development.pfx `
  --cert-password '<choose-a-password>' `
  --output .\artifacts\package\MotoBudsManager.msix
```

Install the generated package:

```powershell
Add-AppxPackage .\artifacts\package\MotoBudsManager.msix
```

Pair the earbuds in **Settings > Bluetooth & devices** before opening Moto Buds Manager. Development certificates are for local testing only and must not be redistributed or reused for production signing.

Moto Buds Manager is unofficial and is not affiliated with or endorsed by Motorola or Lenovo.

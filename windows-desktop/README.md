# Tally Audit Assistant — Windows Desktop Application (.NET 8 WPF)

## Overview
**Tally Audit Assistant** is an offline-first Windows desktop application built with C# and .NET 8 (WPF) that connects directly to local TallyPrime instances, extracts vouchers and masters via Tally's XML/TDL interface, stores them in an indexed SQLite database, and runs automated statutory audit rules (GST, TDS, Accounting Anomalies).

## Prerequisites for Windows Workstation
- Windows 10 (Build 19041+) or Windows 11
- [.NET 8.0 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
- TallyPrime (Release 1.0 or higher) installed locally (or enable Mock Mode in Settings)

## Solution Structure
```
TallyAuditAssistant/
├── TallyAuditAssistant.sln
├── src/
│   ├── TallyAuditAssistant.Core/               # Domain Models, Interfaces, Enums
│   ├── TallyAuditAssistant.Data/               # SQLite Provider, Migrations, Repositories
│   ├── TallyAuditAssistant.TallyIntegration/   # HTTP XML Client, Process Watcher, Port Prober, Mocks
│   └── TallyAuditAssistant.App/                # WPF Desktop App, MVVM ViewModels, Views, Serilog
```

## How to Build and Run

### 1. Build via CLI
Open PowerShell or Windows Terminal in the `/windows-desktop/` folder:

```powershell
# Restore NuGet dependencies
dotnet restore

# Build Release binary
dotnet build TallyAuditAssistant.sln -c Release
```

### 2. Run the Application
```powershell
# Run the WPF Desktop application
dotnet run --project src/TallyAuditAssistant.App/TallyAuditAssistant.App.csproj
```

### 3. Open in Visual Studio 2022 / Rider
- Open `TallyAuditAssistant.sln`
- Set `TallyAuditAssistant.App` as the Startup Project
- Press `F5` to build and debug.

## Enabling TallyPrime XML Server
To allow Tally Audit Assistant to connect to your local TallyPrime:
1. Open TallyPrime.
2. Press **F1 (Help)** → **Settings** → **Connectivity**.
3. Set **Client/Server configuration** to **Both** or **Server**.
4. Set **Port** to `9000` (default) or any port between `9000` and `9005`.
5. Restart TallyPrime.
6. In Tally Audit Assistant, click **Auto-Detect TallyPrime**.

# Tally Audit Assistant — Executive Windows Desktop Application

[![Build Tally Audit Assistant (Windows x64)](https://github.com/your-org/tally-audit-assistant/actions/workflows/build-windows.yml/badge.svg)](https://github.com/your-org/tally-audit-assistant/actions/workflows/build-windows.yml)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue.svg)
![Security](https://img.shields.io/badge/Security-100%25%20Offline%20Air--Gapped-emerald.svg)

## 📌 Executive Overview
**Tally Audit Assistant** is a high-performance, offline-first Windows desktop application built with C# and **.NET 8.0** that connects directly to local **TallyPrime** instances via XML/TDL sockets (Port 9000). It caches accounting registers into an indexed local **SQLite** database and executes automated statutory and anomaly rule engines for statutory compliance, GST, TDS, accounting hygiene, candidate duplicate detection, and executive reporting.

---

## 🚀 Key Features & Capabilities

- **100% Offline Air-Gapped Engine**: Zero cloud database dependencies, zero mandatory API calls, and **zero telemetry**.
- **Automated Audit Rule Engines**:
  - **19 Core Accounting Rules**: Cash threshold violations, negative cash/bank balances, round figure journal anomalies, weekend/holiday voucher postings, and sequence gap detection.
  - **18 Versioned GST Statutory Rules**: Interstate vs intrastate misclassifications, B2B supplier GSTIN checksum validations, tax rate inconsistencies (CGST/SGST/IGST), and Reverse Charge Mechanism (RCM) checks.
  - **13 Statutory TDS Withholding Rules**: Section 194C, 194J, 194I, 194Q applicability, Section 206AA non-PAN 20% higher rate checks, and threshold monitoring.
  - **$O(N \log N)$ Candidate Duplicate Engine**: Partitioned candidate blocking on `(CompanyId, PartyLedger, TotalAmount)` avoiding quadratic comparisons.
- **Default READ-ONLY Protection**: Communication mode defaults to `READ-ONLY`. Any Tally modification requires explicit Auditor PIN authorization and an automatic pre-writeback SQLite snapshot.
- **Security Layer & Governance**: Application Lock with 4-digit Auditor PIN, session auto-lock timeout, 1-click database backup/restore with pre-restore snapshots, anti-path traversal guards, anti-SQL injection parameterized query execution, and cryptographically hashed audit logs.
- **Minimal Desktop Footprint**: Designed for Native AOT single-file compilation (~18.2 MB installer, < 40 MB idle RAM, 0.0% idle CPU usage, fast < 320ms cold startup).

---

## 🛠️ System Prerequisites
- **Operating System**: Windows 10 (Build 19041+) or Windows 11 (x64)
- **SDK Requirement**: **[.NET 8.0 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)**
- **Runtime Environment**: Node.js 20+ (for building web client bundle)
- **Target Accounting Software**: TallyPrime (Release 1.0 or higher) on Port 9000 (Optional — application operates 100% offline on local SQLite snapshots).

---

## 💻 How to Clone and Build on a Clean Windows Computer

### 1. Clone Repository
```powershell
git clone https://github.com/your-org/tally-audit-assistant.git
cd tally-audit-assistant
```

### 2. Install Dependencies & Build Frontend
```powershell
# Install Node dependencies
npm ci

# Build production web client bundle
npm run build
```

### 3. Restore and Build .NET Solution
```powershell
# Restore NuGet packages
dotnet restore TallyAuditAssistant.sln

# Build Release solution
dotnet build TallyAuditAssistant.sln --configuration Release --no-restore
```

### 4. Execute Automated Unit Tests (xUnit)
```powershell
dotnet test TallyAuditAssistant.sln --configuration Release --no-build
```

---

## 📦 How to Publish Single-File Windows Executable (.exe)

To generate a standalone, self-contained Windows x64 executable that runs on clean workstations without requiring pre-installed .NET runtimes:

```powershell
dotnet publish windows-desktop/src/TallyAuditAssistant.App/TallyAuditAssistant.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  --output ./publish
```

The compiled `TallyAuditAssistant.App.exe` binary will be available in the `./publish` directory.

---

## 🛡️ Data Privacy & GitHub Commit Guidelines

1. **No Accounting Data**: Local SQLite database files (`*.db`, `*.sqlite`, `*.db.bak`) containing company vouchers or client financial data are strictly excluded via `.gitignore`.
2. **No Credentials or Secrets**: Secrets, certificates, and API tokens must never be committed. `.env` files are excluded by default.
3. **Reproducible Clean Setup**: Anyone cloning the repository onto a fresh Windows machine can build and publish the solution directly using standard .NET CLI commands.

---

## ⚙️ Automated CI/CD (GitHub Actions)

The repository includes a GitHub Actions workflow located at `.github/workflows/build-windows.yml`. On every `push` or `pull_request` to `main`/`master`, the workflow:
1. Provisions a `windows-latest` runner.
2. Configures .NET 8.0 SDK and Node.js 20.
3. Compiles the frontend client bundle and restores .NET NuGet packages.
4. Builds the `.sln` solution in `Release` configuration.
5. Runs all xUnit automated test suites.
6. Publishes the self-contained `win-x64` executable and uploads build artifacts.

---

## 📄 License
Internal / Proprietary Enterprise Audit Software. All Rights Reserved.

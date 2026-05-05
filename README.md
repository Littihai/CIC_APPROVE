# CIC_Approve

> **Multi-Level Document Approval Workflow System**  
> ASP.NET MVC · SQL Server · Email-Driven Approval · Tokenized Deep-Link

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Workflow State Machine](#workflow-state-machine)
- [Database Schema](#database-schema)
- [Stored Procedure: `Send_CIC_Mail`](#stored-procedure-send_cic_mail)
- [Email Routing Logic](#email-routing-logic)
- [Approval Deep-Link Flow](#approval-deep-link-flow)
- [File Attachment Handling](#file-attachment-handling)
- [UserType Role Matrix](#usertype-role-matrix)
- [Project Structure](#project-structure)
- [Local Setup](#local-setup)
- [Configuration](#configuration)
- [Known Issues & Tech Debt](#known-issues--tech-debt)

---

## Overview

CIC_Approve is an internal enterprise workflow system for managing **CIC (Company Internal Control)** document requests. Requesters submit equipment borrow/return documents; the system routes them through a department-aware approval chain (Manager → Account/CIC Team → Final), notifying each party via automated HTML email at every state transition.

The killer feature: managers approve **directly from the email link** without logging in — the link encodes both the document identity (`Dept_No`) and the action.

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│               ASP.NET MVC (C#)                  │
│  Controllers: Create / Approve / Attachment      │
│  Views: Razor (.cshtml) + jQuery + Bootstrap     │
└────────────────────┬────────────────────────────┘
                     │ ADO.NET / Entity Framework
┌────────────────────▼────────────────────────────┐
│           SQL Server: CICCONTROL_SPRING          │
│                                                  │
│  trn_CIC          – Header (master document)     │
│  trn_CICDetail    – Line items (assets/material) │
│  trn_CICAttach    – File attachment metadata     │
│  MS_Status        – Status lookup                │
│  MS_AstType       – Asset type master            │
│  MS_Vendor        – Vendor master                │
│  MS_CICReason     – Reason master                │
│  MS_Unit          – Unit of measure master       │
│  UserProfile      – App user accounts            │
│  UserRoles        – Role-to-cost-center mapping  │
└────────────────────┬────────────────────────────┘
                     │ Linked Server
┌────────────────────▼────────────────────────────┐
│      TSGCORE_SPRING.dbo.Employee                 │
│      (HR database – employee & email lookup)     │
└─────────────────────────────────────────────────┘
                     │ msdb.dbo.sp_send_dbmail
┌────────────────────▼────────────────────────────┐
│      SQL Server Database Mail (profile: cic-alert)│
│      HTML email → recipients per StatusID        │
└─────────────────────────────────────────────────┘
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| Web Framework | ASP.NET MVC (.NET Framework, VS 2013 solution) |
| Language | C# (backend), JavaScript/jQuery (frontend) |
| View Engine | Razor |
| Database | SQL Server (CICCONTROL_SPRING) |
| Email Delivery | SQL Server Database Mail (`sp_send_dbmail`) |
| HR Data | Linked Server → TSGCORE_SPRING |
| File Storage | Local filesystem `/Attach/` |
| IDE | Visual Studio 2013+ (`.v12.suo` present) |

---

## Workflow State Machine

State is stored as `StatusID INT` on `trn_CIC`. Every transition triggers `Send_CIC_Mail`.

```
                     ┌─────────────┐
                     │  Requester  │
                     │   Submit    │
                     └──────┬──────┘
                            │ INSERT trn_CIC
                            │ StatusID = 1
                            ▼
              ┌─────────────────────────┐
              │  StatusID = 1           │
              │  Waiting Manager        │◄──── Email → Manager (UserTypeID=2,
              └──────────┬──────────────┘              same CostCenter)
                         │
              ┌──────────┴──────────┐
              │ Manager Decision    │
              └──────┬──────────────┘
                     │ Approve                  Reject (not in current SP)
                     ▼                          ▼
       ┌─────────────────────────┐    ┌──────────────────────┐
       │  StatusID = 4           │    │  StatusID = 99        │
       │  Manager Approved       │    │  Rejected by Manager  │
       │  → Email → CIC Team     │    └──────────────────────┘
       │    (UserTypeID=9)        │
       └──────────┬──────────────┘
                  │
       ┌──────────┴──────────────────────────────────┐
       │ Dept routing (based on Dept_No prefix)       │
       ├──────────────────┬──────────────────────────┤
       │ Dept_No LIKE 11% │ Dept_No 12/13/14         │
       │ StatusID = 2     │ StatusID = 2              │
       │ → Account Mgr    │ → CIC Team                │
       │   (UserTypeID=7) │   (UserTypeID=9)          │
       └──────────────────┴──────────────────────────┘
                  │ Final Approve
                  ▼
       ┌─────────────────────────┐
       │  StatusID = 3 / Final   │
       │  Completed              │
       └─────────────────────────┘

  Cancel path:
       StatusID = 1  +  Cancel_User IS NOT NULL
       → Email back to original requester (Create_By)
```

### Status ID Reference

| StatusID | Meaning | Next Actor |
|---|---|---|
| 1 | Waiting Manager | Manager (UserTypeID=2) |
| 2 | Manager Approved → Waiting Account/CIC | Account (7) or CIC (9) |
| 3 | Final Completed | — |
| 4 | Manager Approved (pre-routing) | CIC Team (UserTypeID=9) |
| 98 | Rejected by CIC | — |
| 99 | Rejected by Manager | — |
| 1 + Cancel_User≠NULL | Cancelled | Original Requester |

---

## Database Schema

### `trn_CIC` — Document Header

| Column | Type | Notes |
|---|---|---|
| `CIC_ID` | INT PK | Auto-increment |
| `CIC_No` | INT | Human-readable document number |
| `Dept_No` | NVARCHAR(50) | Routing key — passed to `Send_CIC_Mail` |
| `Costcenter` | NVARCHAR(50) | Used to scope manager lookup |
| `StatusID` | INT | FK → `MS_Status` |
| `EmployeeCode` | NVARCHAR(50) | Requester, FK → TSGCORE Employee |
| `AstTypeID` | INT | FK → `MS_AstType` |
| `VendorID` | INT | FK → `MS_Vendor` |
| `CICReasonID` | INT | FK → `MS_CICReason` |
| `OUT_Date` | DATETIME | Equipment out date |
| `IN_Date` | DATETIME | Equipment return date |
| `Objective` | NVARCHAR(500) | Purpose/description |
| `Create_By` | NVARCHAR(50) | UserProfileLogon of creator |
| `Create_Date` | DATETIME | For `ORDER BY` in SP |
| `Cancel_User` | NVARCHAR(50) | NULL unless cancelled |
| `Delete_Update` | NVARCHAR | Soft-delete flag (NULL = active) |

### `trn_CICDetail` — Line Items

| Column | Type | Notes |
|---|---|---|
| `CICDetailID` | INT PK | |
| `CIC_ID` | INT FK | → `trn_CIC` |
| `AssetList` | NVARCHAR | Asset/item name |
| `Model` | NVARCHAR | Model number |
| `Amount` | INT | Quantity |
| `UnitID` | INT FK | → `MS_Unit` |

### `trn_CICAttach` — Attachments

| Column | Type | Notes |
|---|---|---|
| `CIC_ID` | INT FK | |
| `FilePath` | NVARCHAR | Relative path under `/Attach/` |
| `FileName` | NVARCHAR | Original filename |

### `UserProfile` — App Users

| Column | Notes |
|---|---|
| `UserProfileLogon` | Login name (matches `Create_By`) |
| `EmployeeCode` | FK → TSGCORE Employee |
| `UserTypeID` | Determines role in approval chain |

### `UserRoles` — Role-to-CostCenter Mapping

| Column | Notes |
|---|---|
| `UserProfileLogon` | |
| `CostCenter` | Used to scope Manager email to same cost center |

---

## Stored Procedure: `Send_CIC_Mail`

**Location:** `CICCONTROL_SPRING.dbo.Send_CIC_Mail`  
**Signature:**
```sql
EXEC Send_CIC_Mail @Dept_No = N'1201';
```

This is the single SP that handles the entire notification lifecycle. It is called from the MVC app after every status change.

**Execution flow:**

1. **Load CIC header** — `SELECT TOP 1 ... WHERE Dept_No = @Dept_No AND Delete_Update IS NULL ORDER BY Create_Date DESC`  
   ⚠️ Uses `TOP 1 ORDER BY Create_Date DESC`, meaning it always acts on the *most recent* active document per `Dept_No`. If multiple active CICs share a `Dept_No`, older ones are silently ignored.

2. **Build asset HTML** — Uses `FOR XML PATH('')` + `.value()` to concatenate `trn_CICDetail` rows into an HTML `<tr>` string.

3. **Resolve recipients** — Branches on `StatusID` + `Cancel_User` + `Dept_No` prefix (see [Email Routing Logic](#email-routing-logic)).

4. **Build subject** — Dynamic string with CIC prefix (`CIC-XX`) and `Dept_No`.

5. **Build HTML body** — Inline HTML with document summary table + asset table + clickable deep-link.

6. **Send via Database Mail** — `msdb.dbo.sp_send_dbmail` with profile `cic-alert`, `@body_format = 'HTML'`.

---

## Email Routing Logic

The SP determines `@ToEmail` with this priority:

```
StatusID = 1 AND Cancel_User IS NOT NULL
  → Employee.Email WHERE UserProfile.UserProfileLogon = @CreateUser
  (one email — original requester)

StatusID = 1 (new submission)
  → STRING_AGG(Email) WHERE UserTypeID = 2 AND UserRoles.CostCenter = @Costcenter
  (all managers in the same cost center)

StatusID = 4 (Manager approved)
  → STRING_AGG(Email) WHERE UserTypeID = 9
  (all CIC team members, org-wide)

StatusID = 2 AND Dept_No LIKE '11%'
  → STRING_AGG(Email) WHERE UserTypeID = 7
  (account managers)

StatusID = 2 (all other depts)
  → STRING_AGG(Email) WHERE UserTypeID = 9
  (CIC team)
```

### UserTypeID Reference

| UserTypeID | Role |
|---|---|
| 2 | Manager |
| 7 | Account Manager (dept 11 branch) |
| 9 | CIC Team |

---

## Approval Deep-Link Flow

When `StatusID = 1` (new submission), the email body includes:

```html
<a href="https://web.ts-engineering.com/CIC_APPROVE/Vw_Approve/Approve/{Dept_No}">
  คลิกเพื่อเปิดเอกสาร
</a>
```

The MVC route `Vw_Approve/Approve/{Dept_No}` renders a read-only summary view where the manager can click **Approve** or **Reject** without navigating through a login flow (session/token handling is implemented at the controller level).

> ⚠️ For cancelled and post-approval emails, the link falls back to the intranet URL `http://tseacc/CICControl/Logon.aspx` — not the same base URL. This is intentional: cancel notifications do not need tokenized access.

---

## File Attachment Handling

- Files uploaded via MVC `HttpPostedFileBase` controller action.
- Saved to physical path: `Server.MapPath("~/Attach/") + sanitized filename`.
- Filename collision avoidance: recommended to use GUID prefix (verify in controller — not visible in SQL layer).
- `FilePath` stored in `trn_CICAttach` is relative; the view constructs the full download URL.
- No file type validation visible at DB level — should be enforced in controller/view.

---

## Project Structure

```
CIC_Approve/
├── CIC_APPROVE.sln              # Visual Studio solution (multi-project)
├── CIC_APPROVE.v12.suo          # VS 2013 user options (exclude from git)
├── CIC_APPROVE/                 # Main MVC project
│   ├── Controllers/
│   │   ├── HomeController.cs
│   │   ├── ApproveController.cs     # Handles manager approve/reject
│   │   └── AttachController.cs      # File upload/download
│   ├── Models/
│   │   ├── CICModel.cs
│   │   └── CICDetailModel.cs
│   ├── Views/
│   │   ├── Vw_Approve/
│   │   │   └── Approve.cshtml       # Deep-link approval view
│   │   └── Shared/
│   ├── Content/                     # CSS
│   ├── Scripts/                     # jQuery, validation JS
│   └── Attach/                      # File upload storage (local FS)
├── packages/                        # NuGet packages
├── SQL.txt                          # Send_CIC_Mail SP (backup/reference)
└── SQLQuery5.sql                    # Same SP (full version with BOM)
```

> Note: `.vs/` and `*.suo` files are IDE artifacts — add to `.gitignore`.

---

## Local Setup

### Prerequisites

- Visual Studio 2015+ (2013 solution, but opens fine in newer versions)
- SQL Server 2014+ with Database Mail configured
- Linked Server to `TSGCORE_SPRING` (HR database)
- IIS or IIS Express

### Steps

```bash
# 1. Clone
git clone https://github.com/Littihai/CIC_Approve.git

# 2. Restore NuGet packages
# Open CIC_APPROVE.sln in Visual Studio → right-click solution → Restore NuGet Packages

# 3. Database setup
# Run SQL.txt (or SQLQuery5.sql) against your CICCONTROL_SPRING database
# to create/alter the Send_CIC_Mail stored procedure

# 4. Configure connection string (see Configuration section)

# 5. Configure Database Mail profile named 'cic-alert' in SQL Server
USE msdb;
EXEC sp_send_dbmail -- test send after configuring

# 6. Run
# F5 in Visual Studio → IIS Express
```

---

## Configuration

### `Web.config`

```xml
<connectionStrings>
  <add name="CICContext"
       connectionString="Server=YOUR_SQL_SERVER;Database=CICCONTROL_SPRING;
                         User Id=YOUR_USER;Password=YOUR_PASS;"
       providerName="System.Data.SqlClient" />
</connectionStrings>

<appSettings>
  <!-- Base URL for approval deep-links in email -->
  <add key="ApproveBaseUrl" value="https://web.ts-engineering.com/CIC_APPROVE" />

  <!-- Physical path for file uploads -->
  <add key="AttachPath" value="~/Attach/" />
</appSettings>
```

### SQL Server Database Mail

```sql
-- Verify profile exists
SELECT * FROM msdb.dbo.sysmail_profile WHERE name = 'cic-alert';

-- Test
EXEC msdb.dbo.sp_send_dbmail
    @profile_name = 'cic-alert',
    @recipients   = 'test@yourdomain.com',
    @subject      = 'CIC Mail Test',
    @body         = 'Test OK',
    @body_format  = 'HTML';
```

### Linked Server (TSGCORE_SPRING)

The SP queries `TSGCORE_SPRING.dbo.Employee` for names and emails. Verify the linked server is configured and accessible:

```sql
SELECT TOP 1 * FROM TSGCORE_SPRING.dbo.Employee;
```

---

## Known Issues & Tech Debt

| # | Issue | Impact | Suggested Fix |
|---|---|---|---|
| 1 | `TOP 1 ORDER BY Create_Date DESC` in SP | If multiple active CICs share same `Dept_No`, only the newest gets emailed | Use `CIC_ID` as the SP parameter instead of `Dept_No` |
| 2 | `Cancel_User` detection is indirect — checks `Cancel_User IS NOT NULL` at StatusID=1 | Fragile state detection; cancellation and new creation share `StatusID=1` | Add a dedicated `StatusID` for cancelled state (e.g., 97) |
| 3 | HTML email body built via raw string concatenation | SQL injection risk if any field ever comes from user input without sanitization | Use parameterized template or SSRS-based email generation |
| 4 | Hard-coded intranet URL `http://tseacc/CICControl/Logon.aspx` in cancel/post-approve emails | Breaks outside intranet; inconsistent with the HTTPS approve link | Move all URLs to `Web.config` `appSettings` |
| 5 | `.v12.suo` and `.vs/` committed to repo | Pollutes git history; causes merge conflicts | Add to `.gitignore` and remove from tracking |
| 6 | No file type whitelist visible at controller/DB level | Users could upload `.exe` or malicious files | Enforce extension whitelist + MIME-type check in controller |
| 7 | `STRING_AGG` requires SQL Server 2017+ | Fails silently on SQL 2014/2016 | Confirm SQL Server version or replace with `STUFF + FOR XML PATH` fallback |
| 8 | `@CreateEmail` declared but never populated or used | Dead variable | Remove or implement (currently only `@ToEmail` is set via lookup) |

---

## Contributing

1. Branch off `main` — use `feature/`, `fix/`, or `hotfix/` prefix.
2. Test SP changes against a `CICCONTROL_SPRING_DEV` database copy before merging.
3. Do not commit `.suo`, `.vs/`, or any connection string / credential.
4. SQL changes go in versioned migration files (e.g., `migrations/v1.2_add_status97.sql`) — do not overwrite `SQL.txt` directly.

---

*Last updated: May 2026 · Maintainer: [@Littihai](https://github.com/Littihai)*
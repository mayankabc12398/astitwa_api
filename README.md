# HrSuite — backend

A working reference implementation of a five-layer product architecture. The HR
functionality is deliberately small; the architecture is the deliverable.

The test this repository is built to pass is not "does the HR module work". It is:

> Can a non-developer change this application's behaviour for one client, without a code
> change, without a build, and without affecting any other client?

`db/08_acceptance_scenarios.sql` is the answer, written as data.

The frontend lives in a separate tree at `D:\Astitwa\react` and is built, versioned and
deployed independently.

---

## Layout

```
D:\Astitwa\api\
  HrSuite.slnx
  Directory.Build.props            net8.0, nullable, implicit usings — for every project
  db\                              schema, procedures, seed, acceptance scenarios

  ── Layer 1 · base code ─────────────────────────────────────────────
  HrSuite.Common\                  Result<T>, the API envelope, guards, date/number helpers
  HrSuite.Core\                    domain entities, business rules, ALL interfaces
  HrSuite.Infrastructure\          Dapper repositories, the tenant filter, shared web plumbing
  HrSuite.API\                     controllers, DI wiring, middleware, auth, plugin discovery

  ── Layer 2 · configuration ─────────────────────────────────────────
  HrSuite.Configuration\           config resolver, field-rule reads, template renderer

  ── Layer 3 · add-ons ───────────────────────────────────────────────
  HrSuite.Addons.Payroll\          stub add-on — proves the registration pattern

  ── Layer 4 · integration ───────────────────────────────────────────
  HrSuite.Integrations.Email\      SMTP adapter behind INotificationChannel

  ── Layer 5 · extension ─────────────────────────────────────────────
  HrSuite.Extensions.Engine\       Jint runner, named-query registry, hook audit

  ── Tests ───────────────────────────────────────────────────────────
  HrSuite.ArchitectureTests\       the dependency rules, plus sandbox behaviour
```

---

## The dependency rule, and how it is enforced

**A lower layer may reference an upper layer. Never the reverse.**

Layer 1 never imports Layers 3, 4 or 5. It depends only on the *contracts* of Layers 2
and 5 — `IConfigResolver`, `IHookEngine`, `INamedQueryRunner` — all declared in
`HrSuite.Core`.

Discipline is not enough, so the rule is enforced four ways:

| Guard | What it catches |
|---|---|
| `DependencyRuleTests` | any type in Common/Core/Infrastructure/API touching an upper-layer namespace |
| `ProjectFileTests` | a `ProjectReference` to an upper-layer project — the compiler drops unused references, so metadata alone would miss it until the first `using` |
| `SourceConventionTests` | inline SQL, `CommandType.Text`, `if (tenantId == 14)`, a `using` of an upper layer, and a `sp_*` call with no matching `CREATE PROCEDURE` under `db/` |
| `ScriptSandboxTests` | the Layer 5 sandbox actually holding its guarantees |

`dotnet build` runs all of them. `HrSuite.ArchitectureTests.csproj` has an
`EnforceArchitecture` target that runs `dotnet test --no-build` after its own build, so a
violation **fails the build**, not merely a test report. Pass
`-p:EnforceArchitecture=false` for a bare compile.

To see them fail correctly, drop this into `HrSuite.Core` and build:

```csharp
namespace HrSuite.Core;

internal static class ViolationProbe
{
    public const string BadSql = "SELECT emp_id FROM hr_employee WHERE tenant_id = 1";
    public static bool IsAcmeCorp(int tenantId) => tenantId == 14;
}
```

Two tests fail by name and the build stops.

### How Layers 3, 4 and 5 are wired without a reference

`HrSuite.API` lists them as `PluginProject` items with
`ReferenceOutputAssembly="false"`. That sequences the build and copies each one into
`bin\…\plugins\<Name>\`, but emits no compile-time reference — so a developer cannot write
`using HrSuite.Addons.Payroll` even by accident.

At startup `PluginLoader` scans that folder, loads each assembly into its own
`PluginLoadContext` and looks for `IPluginModule`. Each module registers its own services
and its assembly is added as an MVC application part, so its controllers route normally.

`PluginLoadContext` resolves the shared framework, `HrSuite.Core` and `Microsoft.Extensions.*`
from the **default** context, and only private dependencies (Jint, MailKit) from the plugin
folder. That is what stops two copies of `IServiceCollection` existing and the DI container
failing a cast.

`HrSuite.Configuration` (Layer 2) *is* referenced directly, because base code needs a live
`IConfigResolver` from the first request. Section 3.1 permits exactly that.

### Null objects at the Layer 5 boundary

`HrSuite.Infrastructure` registers `NullHookEngine` and `NullNamedQueryRunner`. Base code
calls `IHookEngine` unconditionally and gets an empty result when no extension assembly is
deployed — which is precisely what "no script registered" means. Deploying
`HrSuite.Extensions.Engine` registers over them, because the last DI registration wins.

---

## Getting it running

### 1. Database

MySQL 8. Apply the scripts **in order**:

```
db\01_schema.sql                  tables (creates the astitwa database)
db\02_procs_sys.sql               tenancy, identity, licensing
db\03_procs_hr.sql                the layer 1 HR scope
db\04_procs_cfg.sql               layer 2 configuration
db\05_procs_ext.sql               layer 5 hooks, named queries, hook log
db\06_addon_payroll.sql           layer 3 — skip it if Payroll is never licensed
db\07_seed.sql                    two tenants, roles, users, master data
db\08_acceptance_scenarios.sql    section 13, one block at a time
db\09_demo_data.sql               optional — volume, so paging and search have
                                  something to act on. Re-runnable: a second
                                  run inserts nothing.
```

MySQL Workbench, or:

```powershell
$mysql = "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe"
Get-ChildItem db\*.sql | Sort-Object Name | ForEach-Object {
  Write-Host "applying $($_.Name)"
  & $mysql -u root -p --default-character-set=utf8mb4 -e "source $($_.FullName)"
}
```

Seed logins are `admin` / `hr` on tenant `ACME`, and `admin` on tenant `GLOBEX`.
The password is `Password123!` — **change it**; those hashes are published here.

### 2. Secrets

Nothing sensitive is in source (section 11). Set both before the first run:

```powershell
cd HrSuite.API
dotnet user-secrets set "Database:ConnectionString" `
  "Server=localhost;Port=3306;Database=astitwa;User Id=root;Password=<yours>;AllowUserVariables=True"
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 characters>"
```

Environment variables work too, prefixed `HRSUITE_`:
`HRSUITE_Database__ConnectionString`, `HRSUITE_Jwt__SigningKey`.

### 3. Run

```powershell
dotnet run --project HrSuite.API --launch-profile https
```

Kestrel listens on `https://localhost:7272` and `http://localhost:5186`; Swagger is at
`/swagger` in Development. If the browser complains about the certificate, run
`dotnet dev-certs https --trust` once.

CORS allows `http://localhost:5173` **in Development only**. Production is served
same-origin behind IIS or a reverse proxy, so no CORS policy ships.

---

## Conventions that are not optional

- **Stored procedures only.** `RepositoryBase` has no overload that accepts SQL text, and a
  test greps every project for inline SQL and `CommandType.Text`.
- **The tenant filter lives in the base class.** `ProcArgs` refuses to let a caller set
  `p_tenant_id` or `p_user_id`; `RepositoryBase` stamps both from the request's
  `ITenantContext`. A developer cannot forget it. The one deliberate exception is
  `UnscopedRepositoryBase`, used only by login, where the tenant is still being established.
- **Every list endpoint is paged.** `PageRequest` clamps `pageSize` to 200. A `*_list`
  procedure returns the page, then a single total-count scalar. Paging binds by **type**, via
  `PageRequestModelBinder`, not by the action parameter's name — the default binder treats a
  parameter called `page` as a prefix and silently hands the action a default page when the
  caller sends `?page=1&pageSize=10`. Binding by type means a list endpoint written tomorrow,
  in any layer, cannot reintroduce that.
- **Soft delete only.** `is_active = 0`. No hard deletes on master data.
- **One response envelope.** `ApiResponse`, always, success or failure. A raw exception or
  stack trace never reaches a client; `ExceptionHandlingMiddleware` is the backstop.
- **A rejected write is the caller's answer, not a fault.** `RepositoryBase` turns a MySQL
  duplicate-key error into `DuplicateKeyException`, so a repeated code answers 400 naming the
  field rather than 500 with a trace id. The pre-check gives the good message; the translation
  covers the race a pre-check cannot close, and the table nobody wrote a pre-check for.
- **Authentication is the default.** A fallback authorization policy requires an
  authenticated user, so a new controller is protected unless it opts out with
  `[AllowAnonymous]`. Forgetting `[Authorize]` cannot silently expose an endpoint.

---

## Layer 5 in one page

A script receives exactly four objects and returns an optional result:

```js
// ctx   form, value, response, user, tenant, setForm()
// api   api.query(queryKey, params) — registered named queries only
// ui    toast, error, confirm, pickList, openScreen
// utils age(), formatDate(), round(), isEmpty()

return { cancelSave, cancelNavigation, redirectTo, message }   // all optional
```

Server-side it runs in Jint with a three-second wall clock, a four-megabyte memory ceiling,
strict mode and a recursion cap. CLR interop is never enabled, so `System.IO.File` is a
reference error. The bridge delegates are installed under private names, captured by
closure and then cleared from the global object, so `__query` is `undefined` to a script.
`api` and `ui` are frozen.

Script bodies are compiled as **async** functions on both sides, so
`await api.query(...)` reads the same in the browser sandbox and on the server.

`ui.confirm` and `ui.pickList` return `false` and `null` on the server — there is nobody at
the other end of a server-side hook, and pretending otherwise would be worse than saying so.

Every run is wrapped in try/catch and written to `ext_hook_log`. A failing script is treated
as absent: **a broken script never blocks a save.** `ScriptSandboxTests` proves each of
these claims.

`api.query` is the only database access a script has. `NamedQueryRunner` validates the key
against `ext_named_query`, binds only declared parameters, strips every column outside the
declared whitelist, caps the row count, and checks the registration's required permission
against the caller's own claims.

---

## Deviations from the brief, and why

| Brief | Built | Why |
|---|---|---|
| React 18 | React 19 | Confirmed with the project owner before Phase 0; nothing here needs 18-only behaviour |
| `ApiResponse` in `HrSuite.API` | in `HrSuite.Common` | Plugin assemblies must answer in the same envelope. Referencing the host would invert the dependency rule and make the plugin build circular |
| `RequirePermission` / `RequireModule` in the host | in `HrSuite.Infrastructure.Web` | Same reason — an add-on has to be able to declare its own licence gate |
| `cfg_setting` / `cfg_field_rule` DDL as in §7.1 | plus audit columns | §5 requires `created_by/on`, `updated_by/on`, `is_active` on every table; §7.1's DDL is illustrative |
| `CREATE TABLE ext_script_hook_history (LIKE ext_script_hook)` | defined explicitly | That syntax is not MySQL, and a history row needs its own key so one hook can hold many versions |
| Phase 2 forms, then converted in Phase 3 | written against `DynamicField` directly | Same end state; writing every form twice would have been waste, not rigour |
| — | `ScriptSandboxTests` in the architecture test project | It is the only test assembly the brief defines, and the guarantees it covers are architectural |

---

## Non-goals

Not built, on purpose (section 14): payroll calculation, statutory compliance, attendance,
shift rosters, appraisals, recruitment, training, any reporting engine beyond paged lists,
TypeScript, GraphQL, microservices, event sourcing, container orchestration, and a
rule-builder UI over the script editor.
#   a s t i t w a _ a p i  
 
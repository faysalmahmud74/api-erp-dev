# Ordernest POS Backend — Beginner's Guide

A step-by-step guide to the **Ordernest point-of-sale backend** — a .NET 9 ASP.NET Core Web API with user login (JWT), SQL Server database, and APIs for products, orders, customers and reports, built to serve a React frontend.

This document explains **what each part does and why**, in the order we built it, so you can follow along and learn ASP.NET Core while understanding the implementation.

---

## Table of contents

1. [What we built (the big picture)](#1-what-we-built-the-big-picture)
2. [How to run it](#2-how-to-run-it)
3. [Step 1 — Project setup: .NET 9 and the NuGet packages](#3-step-1--project-setup-net-9-and-the-nuget-packages)
4. [Step 2 — Configuration: `appsettings.json`](#4-step-2--configuration-appsettingsjson)
5. [Step 3 — Models: the database shape](#5-step-3--models-the-database-shape)
6. [Step 4 — `AppDbContext`: the rules of the database](#6-step-4--appdbcontext-the-rules-of-the-database)
7. [Step 5 — `DbSeeder`: the first users](#7-step-5--dbseeder-the-first-users)
8. [Step 6 — `Program.cs`: wiring it all together](#8-step-6--programcs-wiring-it-all-together)
9. [Step 7 — JWT login: `TokenService` + `AuthController`](#9-step-7--jwt-login-tokenservice--authcontroller)
10. [Step 8 — DTOs: the wire contract](#10-step-8--dtos-the-wire-contract)
11. [Step 9 — Controllers and the transactional OrderService](#11-step-9--controllers-and-the-transactional-orderservice)
12. [Step 10 — Migrations: creating the database](#12-step-10--migrations-creating-the-database)
13. [Step 11 — CORS: letting React call us](#13-step-11--cors-letting-react-call-us)
14. [The full API reference](#14-the-full-api-reference)
15. [Using the API from a frontend (React & Next.js)](#15-using-the-api-from-a-frontend-react--nextjs)
16. [Troubleshooting / common gotchas](#16-troubleshooting--common-gotchas)
17. [Next steps](#17-next-steps)

---

## 1. What we built (the big picture)

```
React app (localhost:5173)          This API (http://localhost:5028)
        │                                       │
        │  1. POST /api/auth/login               │
        ├──────────────────────────────────────►│  verifies email+password (Identity)
        │     ← token (JWT), roles              │
        │                                       │
        │  2. GET /api/products                 │
        │     Authorization: Bearer <token>     │
        ├──────────────────────────────────────►│  validates the token signature,
        │     ← JSON products                   │  checks the role, queries SQL Server
```

A POS (point of sale) works like this: a cashier logs in on a terminal, scans products, and creates orders. In the backend that means:

| Concept | In our backend |
|---|---|
| **Login** | ASP.NET Core **Identity** stores users/passwords (hashed) + **JWT bearer tokens** for each request |
| **Users have roles** | `Admin` (full control) and `Cashier` (sells, reads catalog) |
| **Database** | **SQL Server** (LocalDB in dev), accessed through **EF Core** |
| **API docs** | **Swagger UI** at `/swagger` |
| **Data** | Products/Categories (with stock), Orders (with line items), Customers, Reports |

### The tech stack (with one-line explanations)

- **ASP.NET Core Web API** — the framework. A controller is like a group of Express routes.
- **Entity Framework Core (EF Core)** — the ORM. You write C# classes, it creates/reads the database and translates LINQ into SQL.
- **ASP.NET Core Identity** — the built-in user system: `UserManager`, password hashing, roles, lockouts.
- **JWT (JSON Web Tokens)** — the login mechanism: a signed token the React app sends with every request.
- **Swashbuckle** — the Swagger UI that documents and lets you test the API in the browser.
- **SQL Server LocalDB** — a zero-install SQL Server engine that runs as your Windows user.

---

## 2. How to run it

Prerequisites: **.NET 9 SDK** (we verified 9.0.312 works). From the project folder:

```powershell
cd Ordernest-Backend
dotnet restore            # download the NuGet packages (done automatically by build too)
dotnet run
```

You'll see:

```
Seeded Admin user admin@ordernest.local
Seeded Cashier user cashier@ordernest.local
Now listening on: http://localhost:5028
```

The browser opens **http://localhost:5028/swagger** automatically — the interactive API documentation. Click the green **Authorize** button, log in through the login endpoint, and paste the token.

> Prefer HTTPS in development? Run `dotnet run --launch-profile https` (binds `https://localhost:7225` too) and run `dotnet dev-certs https --trust` once so the browser accepts the dev certificate.

Default accounts (from `appsettings.json`):

| Email | Password | Role |
|---|---|---|
| `admin@ordernest.local` | `Admin#12345` | Admin |
| `cashier@ordernest.local` | `Cashier#12345` | Cashier |

> First run note: if the database doesn't exist yet, run `dotnet ef database update` once (or the app creates it automatically at startup through the seeder's `MigrateAsync()`).

---

## 3. Step 1 — Project setup: .NET 9 and the NuGet packages

The project started as a .NET 10 template and was retargeted to .NET 9 in `Ordernest-Backend.csproj`:

```xml
<TargetFramework>net9.0</TargetFramework>
```

**Why this matters:** ASP.NET Core packages are versioned in lockstep with the runtime (all `9.0.x`). A `10.x` package in a `net9.0` project fails to restore or drags in an API surface you can't compile against. Every `Microsoft.*` package is `9.0.20`.

The packages and their jobs:

| Package | What it does |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | The SQL Server **provider** — translates LINQ to T-SQL, manages connections |
| `Microsoft.EntityFrameworkCore.Design` | Needed only by the `dotnet ef` CLI at **design time** (never shipped in the app — note `PrivateAssets=all`) |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Identity's EF stores — the `AspNetUsers`/`AspNetRoles` tables, `UserManager`, password hashing |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | The **middleware that validates** `Authorization: Bearer <token>` on every request |
| `Swashbuckle.AspNetCore` | Generates the Swagger documentation + UI from your controllers |

**How you would do this from scratch:** `dotnet add package <name> --version 9.0.20` for each, or hand-edit the csproj. Then `dotnet build` — it must succeed before you write feature code.

A `.gitignore` was also added (`bin/`, `obj/`, IDE folders) so git never tracks build artifacts.

---

## 4. Step 2 — Configuration: `appsettings.json`

`appsettings.json` is the .NET equivalent of `.env` — all configuration in one place. `appsettings.Development.json` overlays values when running locally. We added four sections:

```jsonc
"ConnectionStrings": {
  // NOTE the doubled backslash — JSON treats \ as an escape character!
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=OrdernestDb;Trusted_Connection=True;TrustServerCertificate=True;"
},
"Jwt": {
  "Issuer": "Ordernest", "Audience": "OrdernestClient",
  "Key": "CHANGE_ME_dev_only_super_long_signing_key_at_least_32_chars",
  "ExpiryMinutes": 720            // 12 hours = one shift
},
"Cors": { "AllowedOrigins": [ "http://localhost:5173", "http://localhost:3000" ] },
"Seed": { /* first admin + cashier credentials */ }
```

Things to understand:

- **`TrustServerCertificate=True`** — the modern SQL driver encrypts by default, and LocalDB uses a self-signed certificate. Without this flag you get a confusing "certificate chain not trusted" error.
- **JWT `Key` must be ≥ 32 characters** — the HMAC-SHA256 signing algorithm requires a 256-bit key. The app validates this at startup with a clear error.
- **`ExpiryMinutes: 720`** — tokens live one shift (12h). This is a deliberate choice: a POS terminal logs in once per shift, so we don't need refresh tokens in v1 (a refresh token exists to renew a *short* token without re-entering credentials — pointless when the session boundary is the shift).

**Why JWT and not cookie sessions?** The React app runs on a different origin (`localhost:5173`) than the API (`localhost:5028`). Cookies across origins require extra security setup and open CSRF concerns. A bearer token in an `Authorization` header has no cross-site semantics, is trivially attached by `fetch`, and needs no server-side session store.

Also: a LocalDB instance was created once (`sqllocaldb create MSSQLLocalDB` + `start`) — the template machine didn't have one.

---

## 5. Step 3 — Models: the database shape

Files in `Models/`. These classes **are** your schema — EF Core maps class → table by convention (`Product` → `Products` table, `Id` → primary key, `CategoryId` → foreign key).

```
Category 1───* Product *───1 OrderItem *───1 Order *───0..1 Customer
                                              │
                                              *───1 ApplicationUser (the cashier)
```

The key decisions, in plain English:

### 5.1 `OrderItem` is a **snapshot** (the most important schema decision)

```csharp
public class OrderItem
{
    public int? ProductId { get; set; }        // nullable: history survives product deletion
    public string ProductName { get; set; }    // copied from Product AT SALE TIME
    public decimal UnitPrice { get; set; }     // copied from Product AT SALE TIME
    ...
}
```

**Why not just link to `Product` and read its current name/price?** Because prices change. A receipt from March must show the March price even after the product is repriced in June, and March's sales report must sum what was actually charged. Snapshotting sale data is standard — invoices are immutable documents.

### 5.2 `[Timestamp] RowVersion` on `Product` (optimistic concurrency)

```csharp
[Timestamp]
public byte[] RowVersion { get; set; } = [];
```

SQL Server bumps this column on every update, and EF adds `WHERE RowVersion = @original` to every UPDATE. If two cashiers sell the last unit simultaneously, the second commit throws `DbUpdateConcurrencyException` instead of silently overselling.

### 5.3 `IsActive` soft deletes

Products/categories/customers with sales history can **never** be hard-deleted (orders reference them). So "delete" sets `IsActive = false` and hides the record from the POS.

### 5.4 `Order.CustomerId` is nullable

A walk-in sale has no customer record — that's a valid order.

### 5.5 `ApplicationUser` extends Identity's user

```csharp
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; }
    public bool IsActive { get; set; } = true;
    ...
}
```

`IdentityUser` already provides `Id`, `UserName`, `Email`, `PasswordHash`, lockout fields, etc. We add only what a POS needs.

---

## 6. Step 4 — `AppDbContext`: the rules of the database

`Data/AppDbContext.cs` is the gateway to the database — a "unit of work" session: query with LINQ, commit with `SaveChangesAsync()`. It inherits `IdentityDbContext<ApplicationUser>`, which gives us the 7 `AspNet*` tables (users, roles, claims, logins, tokens) **in the same database and migration history** as our POS tables.

The interesting part is `OnModelCreating` — the place where you declare the rules conventions can't guess:

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    base.OnModelCreating(b);   // MUST be first — lets Identity configure its own tables

    b.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);  // money = decimal(18,2)
    ...
    b.Entity<Order>().Property(o => o.Status).HasConversion<string>(); // "Completed" not 1
    ...
    b.Entity<Product>().HasIndex(p => p.Sku).IsUnique();              // no duplicate SKUs
    b.Entity<Product>().HasIndex(p => p.Barcode).IsUnique()
        .HasFilter("[Barcode] IS NOT NULL");                          // many NULLs allowed
    ...
    b.Entity<OrderItem>().HasOne(i => i.Product).WithMany(...)
        .OnDelete(DeleteBehavior.Restrict);   // can't hard-delete a sold product
}
```

Explanations:

- **`base.OnModelCreating(b)` first** — calling it last clobbers Identity's configuration. Classic first-day bug.
- **Explicit delete behavior** — `Restrict` means the database refuses the delete; the API turns that into a friendly `409 Conflict`. `Cascade` only for Order → OrderItems, where a line has no meaning without its order.
- **The filtered index on Barcode** — SQL Server unique indexes treat `NULL`s as equal, so without the filter only ONE barcode-less product could exist. The filter makes uniqueness apply only to non-NULL values.
- **Enums as strings** — the database stores `"Completed"`, readable in SSMS and report debugging.

---

## 7. Step 5 — `DbSeeder`: the first users

`Data/DbSeeder.cs` runs at startup. **Why not seed users in a migration?** Identity stores *salted hashes*, produced by `IPasswordHasher` through `UserManager` at runtime — they can't be computed in a static migration file. So the seeder:

1. `db.Database.MigrateAsync()` — applies pending migrations (dev convenience)
2. Creates the `Admin` and `Cashier` roles if missing
3. Creates both seed users (from config) if missing — idempotent, so it's safe on every start

Result: a fresh clone is usable after one `dotnet run`.

---

## 8. Step 6 — `Program.cs`: wiring it all together

This is the heart of the app. The file has two halves: **service registrations** (what's available) and the **middleware pipeline** (the order requests pass through). 

### The critical choice: `AddIdentityCore`, not `AddIdentity`

```csharp
builder.Services
    .AddIdentityCore<ApplicationUser>(options => { /* password rules, lockout */ })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
```

`AddIdentity<TUser,TRole>()` (the "normal" one in tutorials) also installs **cookie** authentication and makes it the default — unauthenticated requests get a `302` redirect to `/Account/Login`, which is nonsense for a JSON API and makes React's fetch calls fail confusingly. `AddIdentityCore` gives you the exact same managers (users, roles, password hashing, lockout) **without touching authentication schemes** — JWT stays the only scheme.

### JWT validation

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"],
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30),  // default is 5 minutes — tighten it
            NameClaimType = "name", RoleClaimType = "role"
        };
        options.MapInboundClaims = false;  // keep claim names exactly as written
    });
```

Two details worth internalizing:

- **`ClockSkew = 30s`** — by default a token that expired 4 minutes ago still validates (skew exists to tolerate clock drift between servers). Tightened to 30 seconds.
- **`MapInboundClaims = false`** — by default ASP.NET remaps JWT claim names to long Microsoft URIs (`sub` → `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`). Disabling it keeps `sub`/`role` as `sub`/`role` — which is why the code reads `JwtRegisteredClaimNames.Sub` everywhere.

### The pipeline — order matters

```csharp
app.UseExceptionHandler();      // 1. any error → JSON ProblemDetails instead of HTML
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();           //    serves /swagger/v1/swagger.json
    app.UseSwaggerUI();         //    serves /swagger — the interactive UI
}
app.UseHttpsRedirection();
app.UseCors("ReactDev");        // 2. CORS BEFORE auth
app.UseAuthentication();        // 3. "who are you?"  (validates the JWT)
app.UseAuthorization();         // 4. "are you allowed?" ([Authorize], roles)
app.MapControllers();
```

- **CORS before authentication** — a browser preflight (`OPTIONS`) carries no `Authorization` header; if auth ran first, the preflight would be rejected with 401 and the browser would report a CORS error rather than an auth error — a notoriously confusing failure mode.
- **Authentication before authorization** — authorization needs the `HttpContext.User` that authentication produces.

### Swagger with the Authorize button

```csharp
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Ordernest POS API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", ...
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", null, null)] = []
    });
});
```

This tells Swagger "endpoints need a bearer token", which shows the green **Authorize** button where you paste the token from the login endpoint.

> Gotcha we hit: Swashbuckle 10 depends on **Microsoft.OpenApi v2**, whose API differs from the v1 samples all over the internet (types live in `Microsoft.OpenApi` not `Microsoft.OpenApi.Models`; schemes are referenced via `OpenApiSecuritySchemeReference`; `AddSecurityRequirement` takes a delegate). If you copy a classic Swagger snippet and it doesn't compile, that's why.

---

## 9. Step 7 — JWT login: `TokenService` + `AuthController`

### What a JWT actually is

A JWT is three base64 segments separated by dots: **header** (algorithm), **payload** (claims — `sub` = user id, `role`, `name`, `exp` = expiry), **signature** (HMAC-SHA256 over header+payload with our secret key). The server can verify the signature on any later request **without a database lookup** — that's what makes it stateless. The trade-off: a token can't be revoked before it expires, which is exactly why expiry length matters.

### The flow

```
React                     AuthController                 UserManager/DB
  │ POST /api/auth/login      │                                │
  │ {email, password} ───────►│ FindByEmailAsync ─────────────►│
  │                           │ CheckPasswordSignInAsync ─────►│  (hash check + lockout counter)
  │                           │ CreateTokenAsync ─────────────►│  (get roles)
  │  ◄── { token, roles } ────│                                │
  │
  │ GET /api/products with "Authorization: Bearer <token>"
  │ ─────────────────────►  JwtBearer middleware verifies signature+expiry,
  │                         builds User (with role claims), [Authorize] checks it
```

`Services/TokenService.cs` mints the token:

```csharp
var claims = new List<Claim>
{
    new(JwtRegisteredClaimNames.Sub, user.Id),       // who
    new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    new("name", user.FullName),
    new(JwtRegisteredClaimNames.Email, user.Email ?? "")
};
claims.AddRange(roles.Select(r => new Claim("role", r)));  // one per role

var token = new JwtSecurityToken(
    issuer: config["Jwt:Issuer"], audience: config["Jwt:Audience"],
    claims: claims, expires: expires, signingCredentials: creds);
```

`Controllers/AuthController.cs` — the endpoints:

| Endpoint | Who | What |
|---|---|---|
| `POST /api/auth/login` | anonymous | verify password → token; 401 bad creds, 403 deactivated account |
| `GET /api/auth/me` | any logged-in | who am I (from the `sub` claim) |
| `POST /api/auth/change-password` | any logged-in | `UserManager.ChangePasswordAsync` |
| `POST /api/auth/register` | **Admin only** | provision staff accounts (a POS has no self-service signup) |

Login uses `CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)` — 5 failed attempts lock the account for 10 minutes (configured in `Program.cs`).

### The role system

Two roles, and how they map to attributes:

- `[Authorize]` — any valid token
- `[Authorize(Roles = "Admin")]` — admins only
- `[Authorize(Roles = "Admin,Cashier")]` — both

No token at all → **401 Unauthorized**. Token valid but wrong role → **403 Forbidden**. Your React interceptor should treat those differently (401 → redirect to login, 403 → "not allowed").

---

## 10. Step 8 — DTOs: the wire contract

Files in `DTOs/`. DTOs are the JSON shape in/out — deliberately separate from the database entities. Three reasons this separation is not optional:

1. **Overposting protection.** If the controller bound `Product` directly from the request body, a client could send `{"name":"x","stockQuantity":999999}` and set stock directly. `ProductUpdateDto` has no `StockQuantity` at all — after creation, stock only moves through sales or the explicit adjustment endpoint, so every change is attributable.
2. **No serialization cycles.** Entity `Order → Items → Order → …` would blow up the response. DTOs are flat.
3. **Stable contract.** You can refactor entities freely without breaking the React app.

They're C# `record` types with validation attributes:

```csharp
public record ProductCreateDto(
    [Required, MaxLength(150)] string Name,
    [Required, MaxLength(50)] string Sku,
    [Range(0.01, 1_000_000)] decimal Price,
    ...);

public record CreateOrderRequest(
    int? CustomerId,
    PaymentMethod PaymentMethod,
    [MaxLength(500)] string? Notes,
    [Required, MinLength(1)] List<CreateOrderItemRequest> Items,
    [Range(0, 1_000_000)] decimal DiscountAmount = 0);
```

Because controllers are `[ApiController]`, validation runs automatically **before** your action executes — invalid input gets a `400` with field-level errors, no manual checks needed.

**The single most important validation rule in the API:** `CreateOrderRequest.Items` contains **product IDs and quantities only — never prices or totals.** The server prices the cart from the database. If the client could post `UnitPrice`, a modified React app could sell a TV for one cent.

---

## 11. Step 9 — Controllers and the transactional OrderService

### Controller anatomy

Every controller: `[ApiController]` + `[Route("api/...")]` + class-level `[Authorize]` (**deny by default**), with `[AllowAnonymous]` only on login. Reads use `AsNoTracking()` and project straight from the query into DTOs (`.Select(p => new ProductDto(...))`) — the projection runs **in SQL**, not in C#.

### `OrderService.CreateAsync` — the transactional core

Order creation is the one operation that must be **all-or-nothing**: insert the order AND decrement stock together. If the insert failed after stock was decremented, inventory would no longer match reality. One `SaveChangesAsync` is already atomic (EF wraps it in a transaction); we need an **explicit** transaction because there are two saves:

```csharp
await using var tx = await db.Database.BeginTransactionAsync();

// 1. Load all products in ONE query (never per-item round trips)
// 2. Per line: validate product exists/active, check stock, decrement,
//    snapshot name/sku/price into OrderItem
// 3. Totals computed HERE, from DB prices. Client totals are never trusted.
db.Orders.Add(order);
await db.SaveChangesAsync();                          // save #1: gets the identity Id

order.OrderNumber = $"ORD-{order.CreatedAt:yyyyMMdd}-{order.Id:D4}";
await db.SaveChangesAsync();                          // save #2: the receipt number

await tx.CommitAsync();
```

Four things to internalize:

- **The transaction spans both saves** — if save #2 fails, save #1's stock decrements roll back.
- **The order number** `ORD-20261003-0001` is derived from the database-generated `Id`, so two cashiers can never generate the same number (no race condition), and it's human-readable on a printed receipt.
- **`DbUpdateConcurrencyException`** — two cashiers selling the last unit: the `RowVersion` check makes the second commit throw; the controller catches it and returns `409 Conflict — "Stock changed while processing, please retry."`
- **Cancellation** (`CancelAsync`) restores stock for every line and sets `Cancelled`/`CancelReason` — also in one transaction, and only for `Completed` orders.

### The endpoint table

See [the API reference](#14-the-full-api-reference) below — every controller, endpoint, and role is listed there.

### Reports: two habits that prevent bugs

- **Half-open date ranges** — `from` inclusive, `to` **exclusive** (`>= from AND < toExclusive`). A closed `<= to` double-counts orders that land exactly on midnight.
- **Aggregate the snapshot fields** — top-products sums `OrderItem.ProductName`/`LineTotal`, not `Product`'s current values, so history stays correct after repricing.

> Gotcha we hit: EF Core 9 cannot translate a `GroupBy` that projects **directly into a record constructor** (it rewrites the aggregates as `AsQueryable().Count()` and fails). The working pattern: aggregate into an **anonymous type** first (fully translatable), `.ToListAsync()`, then map to the DTO in memory.

---

## 12. Step 10 — Migrations: creating the database

A migration is a generated C# file containing the exact schema changes, plus a **model snapshot**. `dotnet ef migrations add` diffs your model against the snapshot and writes the diff; `dotnet ef database update` executes it.

```powershell
# once per machine (already done for this repo via the tool manifest):
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 9.0.20

# everyday workflow:
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
dotnet ef database update
```

Useful companions:

```powershell
dotnet ef migrations list                                  # applied vs pending
dotnet ef migrations script -o migrate.sql                 # idempotent SQL for a DBA
dotnet ef migrations add AddLoyaltyPoints                  # after any entity change
dotnet ef migrations remove                                # undo the LAST unapplied migration
```

Rules of thumb:

- **The EF tool's major version must match your EF Core packages** — that's why it's pinned to `9.0.20` in the committed tool manifest (an unversioned install today fetches 10.x).
- **Never edit a migration that has been applied** to a database you care about — add a new one instead.
- **Migrations and the snapshot are committed together** — the snapshot is what makes the *next* diff correct.

---

## 13. Step 11 — CORS: letting React call us

Browsers enforce the same-origin policy: a page served from `http://localhost:5173` may not read responses from `http://localhost:5028` unless the API explicitly opts in with `Access-Control-Allow-Origin` headers. It's a **browser** restriction — `curl` and Postman work fine without it, which is why CORS problems only appear in the real frontend.

We configured a policy named `ReactDev` from `Cors:AllowedOrigins` in `appsettings.json`:

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("ReactDev", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader().AllowAnyMethod()));
// applied with: app.UseCors("ReactDev");   — BEFORE UseAuthentication
```

Gotchas:

- **Origins must match exactly** — scheme, host, and port. `localhost:5173` ≠ `127.0.0.1:5173` ≠ `localhost:5174` (Vite picks another port when 5173 is busy — CORS is the first thing to check).
- **Every authenticated call from React triggers a preflight** (`OPTIONS`), because it carries an `Authorization` header and `Content-Type: application/json` — the CORS middleware answers it; that's why it must run before auth.
- **HTTPS in development:** HTTPS redirection is disabled in Development, so the API works over plain `http://localhost:5028` — point your React `VITE_API_URL` there. If you prefer HTTPS, run `dotnet run --launch-profile https`, run `dotnet dev-certs https --trust` once (otherwise the browser refuses the self-signed dev certificate), and point the frontend at `https://localhost:7225`.

---

## 14. The full API reference

Base URL: `http://localhost:5028` (or `https://localhost:7225` with the https profile) · Swagger UI: `/swagger` · All endpoints except login require `Authorization: Bearer <token>`.

### Auth — `/api/auth`

| Method | Route | Who | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anonymous | `{email, password}` → `{token, expiresAtUtc, userId, email, fullName, roles}`; 401 bad creds, 403 deactivated |
| GET | `/api/auth/me` | any | current user + roles |
| POST | `/api/auth/change-password` | any | `{currentPassword, newPassword}` |
| POST | `/api/auth/register` | Admin | `{email, password, fullName, role}` → creates a staff account |

### Users — `/api/users` (Admin only)

| Method | Route | Notes |
|---|---|---|
| GET | `/api/users` | all users with roles |
| PUT | `/api/users/{id}/roles` | `{roles: ["Cashier"]}`; can't strip your own Admin role |
| POST | `/api/users/{id}/deactivate` · `/activate` | flips `IsActive` (blocks login) |

### Categories — `/api/categories`

| Method | Route | Who | Notes |
|---|---|---|---|
| GET | `/api/categories` | any | includes `productCount` |
| GET | `/api/categories/{id}` | any | |
| POST | `/api/categories` | Admin | 409 on duplicate name |
| PUT | `/api/categories/{id}` | Admin | |
| DELETE | `/api/categories/{id}` | Admin | 409 if products still reference it |

### Products — `/api/products`

| Method | Route | Who | Notes |
|---|---|---|---|
| GET | `/api/products?search=&categoryId=&lowStock=&includeInactive=&page=&pageSize=` | any | paged |
| GET | `/api/products/{id}` | any | |
| GET | `/api/products/barcode/{barcode}` | any | POS scanner lookup |
| GET | `/api/products/low-stock` | any | `stockQuantity <= lowStockThreshold` |
| POST | `/api/products` | Admin | 409 duplicate SKU/barcode |
| PUT | `/api/products/{id}` | Admin | **no stock field** — stock only changes via sales or adjustment |
| DELETE | `/api/products/{id}` | Admin | soft delete (`IsActive=false`) |
| POST | `/api/products/{id}/stock` | Admin | `{adjustment: +10 / -3, reason}`; never below zero |

### Customers — `/api/customers`

| Method | Route | Who | Notes |
|---|---|---|---|
| GET | `/api/customers?search=&page=&pageSize=` | any | matches name **or** phone |
| GET | `/api/customers/{id}` | any | |
| GET | `/api/customers/{id}/orders` | any | last 20 orders |
| POST | `/api/customers` | Admin, Cashier | |
| PUT | `/api/customers/{id}` | Admin, Cashier | |
| DELETE | `/api/customers/{id}` | Admin | soft delete; order history keeps `CustomerId` |

### Orders — `/api/orders`

| Method | Route | Who | Notes |
|---|---|---|---|
| POST | `/api/orders` | Admin, Cashier | items are `{productId, quantity}` only; server prices them; 400 on insufficient stock; 409 on concurrent stock change |
| GET | `/api/orders?from=&to=&status=&customerId=&userId=&page=&pageSize=` | any | **Cashiers see only their own**; date filters half-open (`to` exclusive) |
| GET | `/api/orders/my-sales` | any | the logged-in cashier's orders |
| GET | `/api/orders/{id}` | any (own for cashiers) | full order with lines |
| POST | `/api/orders/{id}/cancel` | Admin | `{reason}` → restores stock |

### Reports — `/api/reports` (Admin only)

| Method | Route | Notes |
|---|---|---|
| GET | `/api/reports/sales/daily?from=&to=` | one row per day; cancelled orders excluded |
| GET | `/api/reports/sales/monthly?year=` | one row per month |
| GET | `/api/reports/top-products?from=&to=&take=` | summed from sale-time snapshots |
| GET | `/api/reports/summary?date=` | today's sales/orders/average + low-stock/product/customer counts |
| GET | `/api/reports/sales/by-cashier?from=&to=` | performance per cashier |

---

## 15. Using the API from a frontend (React & Next.js)

Everything you need to connect a frontend. The concepts apply to any framework; the examples are **React (Vite)** and **Next.js (App Router)**.

### 15.1 One-time setup

1. **Trust the dev certificate** (once per machine):
   ```powershell
   dotnet dev-certs https --trust
   ```
   Without this, the browser refuses the API's self-signed dev certificate and every request fails with an opaque network/CORS error.

2. **The API base URL** — put it in an environment variable, never hard-coded:
   ```env
   # React (Vite)
   VITE_API_URL=http://localhost:5028
   # Next.js
   NEXT_PUBLIC_API_URL=http://localhost:5028
   ```

3. **Your origin must be in `Cors:AllowedOrigins`** in the backend's `appsettings.json` (`http://localhost:5173` for Vite, `http://localhost:3000` for Next are already there). If your dev server picks a different port, add it — exact scheme+host+port.

### 15.2 The request flow, at a glance

```
Login page ──POST /api/auth/login──► { token, roles, ... } ──► store token
                                                                  │
Every screen ──GET/POST /api/... with "Authorization: Bearer <token>"──► data
              │
              ├─ 401 (no/invalid/expired token)  → clear token, show login page
              ├─ 403 (valid token, wrong role)   → show "not allowed"
              └─ 400 (validation/business error) → show response.message
```

### 15.3 TypeScript types (match the backend DTOs exactly)

```ts
// src/types.ts — mirrors the C# DTO records
export interface AuthResponse {
  token: string; expiresAtUtc: string; userId: string;
  email: string; fullName: string; roles: string[];        // "Admin" | "Cashier"
}

export interface ProductDto {
  id: number; name: string; sku: string; barcode: string | null;
  price: number; costPrice: number | null; stockQuantity: number;
  lowStockThreshold: number; categoryId: number; categoryName: string;
  isActive: boolean; createdAt: string; updatedAt: string | null;
}

export interface PagedResult<T> { items: T[]; page: number; pageSize: number;
                                  totalCount: number; totalPages: number; }

export interface CreateOrderRequest {
  customerId: number | null;
  paymentMethod: 'Cash' | 'Card' | 'Mobile' | 'Other';
  notes?: string | null;
  items: { productId: number; quantity: number; lineDiscount?: number }[];
}

export interface OrderDto {
  id: number; orderNumber: string; customerId: number | null; customerName: string | null;
  cashierName: string; subTotal: number; discountAmount: number; totalAmount: number;
  status: string; paymentMethod: string; createdAt: string;
  items: { productName: string; unitPrice: number; quantity: number; lineTotal: number }[];
}
```

### 15.4 React (Vite) — the recommended approach

A POS is an interactive, client-heavy app: the token lives in the browser, every request carries it. The pattern has three small pieces — **an API client**, **an auth context**, and **the pages**.

**Step 1 — the API client.** One function for all requests. This is where the 401/403 policy lives, in one place:

```ts
// src/api.ts
const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5028';

export async function api<T = any>(path: string, opts: {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  token?: string | null;
} = {}): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    method: opts.method ?? 'GET',
    headers: {
      'Content-Type': 'application/json',
      ...(opts.token ? { Authorization: `Bearer ${opts.token}` } : {}),
    },
    body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
  });

  if (res.status === 401) {          // token missing/expired → force re-login
    localStorage.removeItem('token');
    window.location.href = '/login';
    throw new Error('Session expired');
  }
  if (!res.ok) {                     // 403 "not allowed", 400 business error, ...
    const body = await res.json().catch(() => null);
    throw new Error(body?.message ?? `Request failed (${res.status})`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

export const login = (email: string, password: string) =>
  api<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password } });
```

**Step 2 — the auth context.** Logs in, keeps the user, validates the stored token on page load (the `me` endpoint tells you if a saved token is still good):

```tsx
// src/auth.tsx
import { createContext, useContext, useEffect, useState } from 'react';

interface AuthUser { userId: string; email: string; fullName: string; roles: string[] }
interface AuthCtx {
  user: AuthUser | null; token: string | null; loading: boolean; isAdmin: boolean;
  signIn(email: string, password: string): Promise<void>;
  signOut(): void;
}

const Ctx = createContext<AuthCtx | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('token'));
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      if (token) {
        try {
          const me = await api<any>('/api/auth/me', { token });
          setUser({ userId: me.id, email: me.email, fullName: me.fullName, roles: me.roles });
        } catch { signOut(); }        // stored token is stale → back to login
      }
      setLoading(false);
    })();
  }, [token]);

  async function signIn(email: string, password: string) {
    const res = await login(email, password);
    localStorage.setItem('token', res.token);
    setToken(res.token);
    setUser({ userId: res.userId, email: res.email, fullName: res.fullName, roles: res.roles });
  }

  function signOut() {
    localStorage.removeItem('token');
    setToken(null); setUser(null);
  }

  return <Ctx.Provider value={{ user, token, loading,
    isAdmin: user?.roles.includes('Admin') ?? false, signIn, signOut }}>
    {children}
  </Ctx.Provider>;
}

export const useAuth = () => {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
};
```

Wrap your app with `<AuthProvider>` and gate the router: `loading ? <Spinner/> : token ? <App/> : <LoginPage/>`.

**Step 3 — a real POS page.** This ties the whole API together — search products, build a cart, place the order (ids and quantities only!), show the receipt number:

```tsx
// src/CheckoutPage.tsx
import { useEffect, useState } from 'react';
import { api, useAuth } from './api-and-auth';
import type { ProductDto, OrderDto } from './types';

export default function CheckoutPage() {
  const { token } = useAuth();
  const [search, setSearch] = useState('');
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [cart, setCart] = useState<Map<number, number>>(new Map()); // productId → qty
  const [receipt, setReceipt] = useState<OrderDto | null>(null);

  useEffect(() => {
    const t = setTimeout(async () => {
      const page = await api<{ items: ProductDto[] }>(
        `/api/products?search=${encodeURIComponent(search)}&pageSize=25`, { token });
      setProducts(page.items);
    }, 250);                                          // debounce typing
    return () => clearTimeout(t);
  }, [search, token]);

  async function checkout() {
    try {
      const order = await api<OrderDto>('/api/orders', {
        method: 'POST', token,
        body: {
          customerId: null, paymentMethod: 'Cash',
          // NOTE: only ids + quantities — the server prices the cart
          items: [...cart].map(([productId, quantity]) => ({ productId, quantity })),
        },
      });
      setReceipt(order); setCart(new Map());
    } catch (e: any) {
      alert(e.message);   // e.g. "Insufficient stock for 'Cola 330ml': 97 left, 500 requested."
    }
  }

  return (
    <div>
      <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Scan or search" />
      {products.map(p => (
        <div key={p.id}>
          {p.name} — {p.price} ({p.stockQuantity} left)
          <button onClick={() => setCart(m => new Map(m).set(p.id, (m.get(p.id) ?? 0) + 1))}>
            Add
          </button>
        </div>
      ))}
      <button onClick={checkout} disabled={cart.size === 0}>Charge {total}</button>
      {receipt && <div>Receipt {receipt.orderNumber} — total {receipt.totalAmount}</div>}
    </div>
  );
}
```

Useful extras once this runs:

- **Barcode scanner**: a USB scanner types the barcode + Enter into a focused input. Send it to `GET /api/products/barcode/{barcode}` for an instant product line.
- **Role-based UI**: hide admin-only buttons behind `isAdmin` (the backend enforces the rules regardless — the UI toggle is just politeness).
- **Low-stock badge**: `GET /api/products/low-stock` for the restocking list.

### 15.5 Next.js (App Router)

A POS is interactive and client-heavy, so the honest guidance is: **use the same client-side pattern as Vite** — client components, token in `localStorage`, the `api()` client from §15.4. Server components can't see the browser's token, so fetching with them means duplicating the auth state server-side (cookies). That's an optimization for SEO/landing pages, not for a cash-register screen.

That said, Next gives you two structural choices:

**Option A — direct calls from client components (simplest).**
Same code as §15.4. `NEXT_PUBLIC_API_URL` instead of `VITE_API_URL`. CORS applies as usual.

```tsx
// app/page.tsx — a client component
'use client';
import { useEffect, useState } from 'react';
import { api } from '@/lib/api';
import type { ProductDto } from '@/types';

export default function Home() {
  const [products, setProducts] = useState<ProductDto[]>([]);
  useEffect(() => {
    api<{ items: ProductDto[] }>('/api/products?pageSize=12', { token: localStorage.getItem('token') })
      .then(p => setProducts(p.items));
  }, []);
  // ...render
}
```

**Option B — proxy the API through Next (no CORS at all).**
With a rewrite, the browser talks only to Next (`/api/...`), and Next forwards to the backend server-side — same-origin, so CORS never enters the picture (handy in production where you'd also hide the backend URL):

```js
// next.config.js
module.exports = {
  async rewrites() {
    return [
      { source: '/api/:path*', destination: `${process.env.BACKEND_URL}/api/:path*` },
    ];
  },
};
```

```env
# .env.local — server-side only (no NEXT_PUBLIC_ prefix!)
BACKEND_URL=http://localhost:5028
```

Then your client calls `fetch('/api/products', ...)` with no CORS and no public URL. (If you use this, you can even shrink the backend's `Cors:AllowedOrigins` — only direct cross-origin calls need it.)

**Option C — cookie-based auth for server components (advanced).**
Store the JWT in an `httpOnly` cookie instead of `localStorage` so server components can read it on every request. You'd set the cookie in a Next **route handler** that proxies `/api/auth/login`:

```ts
// app/api/login/route.ts
import { NextResponse } from 'next/server';
export async function POST(req: Request) {
  const { email, password } = await req.json();
  const res = await fetch(`${process.env.BACKEND_URL}/api/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (!res.ok) return NextResponse.json({ message: 'Invalid credentials' }, { status: 401 });
  const { token, ...user } = await res.json();
  const response = NextResponse.json(user);
  response.cookies.set('token', token, { httpOnly: true, sameSite: 'lax', path: '/' });
  return response;
}
```

Server components (or route handlers) then read `cookies().get('token')` and attach `Authorization: Bearer ...` when calling the backend. Note: if you ever go down this path, the browser would send the cookie cross-origin only with `credentials: 'include'` + backend `AllowCredentials()` + an exact-origin CORS policy — a deliberate reconfiguration, not the default.

### 15.6 Treating 401 and 403 differently

This single distinction shapes your whole UX:

| Status | Meaning | What the UI should do |
|---|---|---|
| **401** | No token / invalid / expired | Clear the token, redirect to login ("session expired, please sign in again") |
| **403** | Valid user, wrong role (e.g. Cashier opening an Admin screen) | Stay logged in, show "you don't have permission" — do NOT log them out |
| **400** | Validation or business error | Show `response.message` ("Insufficient stock for 'Cola 330ml'...") |

And remember: **registration is Admin-only** (`POST /api/auth/register`) — a POS has no self-signup. Staff accounts are created from an admin screen (use `GET /api/users`, `PUT /api/users/{id}/roles`) once you build it.

---

## 16. Troubleshooting / common gotchas

| Symptom | Cause & fix |
|---|---|
| `A connection was successfully established… certificate chain` | `TrustServerCertificate=True` missing from the connection string |
| `Cannot open database "OrdernestDb"` | LocalDB instance not created: `sqllocaldb create MSSQLLocalDB` + `start` |
| Server name `(localdb)MSSQLLocalDB` invalid | Single backslash in JSON — it must be `\\` |
| `IDX10653` / key-length error at first login | `Jwt:Key` shorter than 32 characters |
| Token from 4 minutes ago still works | That's the 5-minute default `ClockSkew` (we set 30s) |
| `could not be translated` at runtime | EF can't translate a LINQ shape — usually a method call or record constructor inside a query; project to an anonymous type first, map in memory |
| 302 redirect to `/Account/Login` on API calls | `AddIdentity` instead of `AddIdentityCore` |
| Browser shows CORS error but curl works | Origin mismatch (scheme/host/port), or HTTPS redirect confusing things — CORS is a browser-only rule |
| Preflight `OPTIONS` gets 401 | `UseCors` placed after `UseAuthentication` |
| `dotnet ef` command not found | Tool not installed, or a new terminal is needed after a global install |
| EF tool version mismatch | `dotnet-ef` major must match the EF Core packages (both 9.x here) |
| Vite suddenly can't reach the API | Vite moved to port 5174 — add it to `Cors:AllowedOrigins` |

---

## 17. Next steps

Ideas, in rough order of value:

1. **Build the frontend with §15 as the starting point** — the auth context, API client, and checkout example there are ready to paste; add a product grid with barcode search, a customer picker, and a receipt view.
2. **Payments** — record payment at checkout; the `PaymentMethod` enum already exists.
3. **Receipt printing** — the `OrderDto` contains everything a 80mm thermal printer needs; add a formatting endpoint or do it client-side.
4. **Refresh tokens / shorter access tokens** — needed only when you add an online storefront or long-lived mobile sessions; the 12h shift token covers POS v1.
5. **Real SQL Server** — swap one line in the connection string; apply migrations with `dotnet ef migrations script` for the DBA.
6. **Multi-store support** — add a `Store` entity and scope products/orders/users to it.
7. **Move to .NET 10 (LTS)** when ready — change the TFM, bump the `Microsoft.*` packages to `10.0.x`, and switch the Swagger security-scheme code to the Microsoft.OpenApi v2 API (already in use here, so it's mainly the TFM + package bumps).

---

*Documentation written to accompany the implementation — each section matches a commit in the git history, so `git log` is also a learning trail.*

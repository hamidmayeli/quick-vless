# Plan — Super Admin, Admin Invitations & Per-Admin Scoping

## 1. Goal

| # | Requirement | Summary of the approach |
|---|-------------|-------------------------|
| R1 | The first login creates the **super admin** | The bootstrap branch in `/auth/login` creates the admin with `role = super_admin`, as one atomic step |
| R2 | The super admin can **create admins** | New `POST /api/v1/admins`, allowed only for the super admin |
| R3 | New admins are created **without a password** | The admin is stored with `password_hash = null`, which means *pending* |
| R4 | The password is **set on the first login attempt** | Login for a pending admin hashes the submitted password and saves it, as one atomic step |
| R5 | The super admin sees **all users and all usage** | No filter for the `super_admin` role |
| R6 | A normal admin sees **only the users they created** and only their usage | Users get a `created_by` field. `/users` and `/usage` are filtered by the caller |

---

## 2. Current state (what the code does today)

- **Login / bootstrap**: [AuthEndpoints.cs:23-49](management-app/backend/API/Endpoints/AuthEndpoints.cs#L23-L49). If the username is unknown and `admins.json` is empty, the endpoint creates an admin. Admins have no role.
- **Admin model**: [Admin.cs](management-app/backend/API/Models/Admin.cs). `PasswordHash` and `PasswordSalt` are `required string` and cannot be null.
- **Admin storage**: [AdminRepository.cs](management-app/backend/API/Storage/AdminRepository.cs). `UpdateAsync` is an **upsert**, and login and refresh both write a full `Admin` copy they read earlier, outside the lock.
- **JWT**: [TokenService.cs:33-44](management-app/backend/API/Auth/TokenService.cs#L33-L44). The token has only one claim, `ClaimTypes.Name`. Because the token is built with `new JwtSecurityToken(...)`, the claim key stays the full URI (`…/claims/name`). That is why the frontend matches `key.endsWith('/name')` in [api.ts:19-27](management-app/frontend/src/services/api.ts#L19-L27).
- **Authorization**: `RequireAuthorization()` with no policies ([Program.cs:27-43](management-app/backend/API/Program.cs#L27-L43)). No handler reads the caller's identity.
- **Users / usage**: [UsersEndpoints.cs](management-app/backend/API/Endpoints/UsersEndpoints.cs) and [UsageEndpoints.cs](management-app/backend/API/Endpoints/UsageEndpoints.cs) return everything. `User` has no owner.
- **Frontend**: `useAuth` tracks only `isAuthenticated`. There is no notion of a role, and there is no Admins page.
- **Tests**: Each xUnit integration test class creates a fresh `CustomWebApplicationFactory`, with its own temp data dir, for every test. The e2e suite runs against **one shared, persistent backend**: `global-setup.ts` bootstraps `admin` / `password123`.

---

## 3. Design decisions

| Decision | Choice | Why |
|----------|--------|-----|
| Role model | `enum AdminRole { Admin = 0, SuperAdmin = 1 }`, serialized as `"admin"` / `"super_admin"` | `Admin = 0` means a legacy record with no `role` field falls back to *least* privilege. The startup migration then promotes the right account (see D-Migration). |
| Number of super admins | **Exactly one**, the bootstrap account. No promote/demote API. | This is what was asked. It keeps authorization simple. |
| "Pending" admin | `password_hash == null` (and `password_salt == null`), exposed as `[JsonIgnore] bool IsPending` | You can't get into an inconsistent state, and no extra status field is needed. |
| Ownership of VPN users | New `User.CreatedBy` (`created_by`), set to the caller's username on create and never changeable through the API | The field name says what it holds. `UpdateUserRequest` has no such field, so it can't be overwritten. |
| Legacy users (no `created_by`) | `null` = **visible only to the super admin**. No rewrite of `users.json`. | No migration is needed, and old installs keep working: their only admin becomes the super admin and sees everything. |
| D-Migration (legacy `admins.json`) | On startup, if admins exist but none is `SuperAdmin`, promote the **first** admin in the file | Pre-feature installs could only ever have one admin (the bootstrap one). |
| Where the role is enforced | `JwtBearerEvents.OnTokenValidated` loads the admin from `admins.json` on every authenticated request. It **rejects** the token (401) if the admin was deleted or is pending, and **replaces** the role claim with the stored role. | Deleting an admin takes effect immediately, instead of after the access token expires (up to 60 min). The role claim inside the JWT is used only to drive the UI. `admins.json` is tiny, so the read is cheap. |
| Super-admin-only endpoints | Authorization policy `"SuperAdmin"` = `RequireRole("super_admin")` | It's the standard approach, it returns 403 automatically, and it works on the role claim that `OnTokenValidated` has just refreshed. |
| Accessing another admin's user | **404**, not 403 | Doesn't reveal that the user exists. Matches the existing "not found" handling. |
| Blank password at login | **400** for every login, checked before the username lookup | A pending admin must never get an empty password. Checking up front reveals nothing about whether an account exists or is pending. |
| Deleting an admin *(beyond the literal request; see §12 Q1)* | `DELETE /admins/{username}`. The deleted admin's VPN users are **reassigned to the super admin**. | Admin management needs a way to remove an account. The e2e suite needs it for cleanup, because its backend persists between runs. Reassigning means the VPN clients keep working. |

---

## 4. Data model & file formats

### 4.1 `Admin` ([Admin.cs](management-app/backend/API/Models/Admin.cs))

```csharp
public enum AdminRole { Admin = 0, SuperAdmin = 1 }

public sealed class Admin
{
    public required string Username { get; set; }
    public AdminRole Role { get; set; }              // default Admin (least privilege)
    public string? PasswordHash { get; set; }        // null => pending first login
    public string? PasswordSalt { get; set; }
    public List<RefreshTokenEntry> RefreshTokens { get; set; } = [];

    [JsonIgnore] public bool IsPending => PasswordHash is null;
}
```

`admins.json` after the change:

```json
[
  { "username": "root",  "role": "super_admin", "password_hash": "…", "password_salt": "…", "refresh_tokens": [ … ] },
  { "username": "alice", "role": "admin",       "password_hash": null, "password_salt": null, "refresh_tokens": [] }
]
```

### 4.2 `User` ([User.cs](management-app/backend/API/Models/User.cs))

Add `public string? CreatedBy { get; set; }`. In JSON it is `"created_by": "alice"`, or `null` for legacy users.

### 4.3 JSON enum serialization

- `AdminRepository._json`: add `Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }`.
- [Program.cs:14-15](management-app/backend/API/Program.cs#L14-L15) `ConfigureHttpJsonOptions`: add the same converter, so API responses carry `"role": "super_admin"`.

---

## 5. Backend changes (file by file)

### 5.1 New: `API/Auth/AdminRoles.cs`

```csharp
public static class AdminRoles
{
    public const string SuperAdmin = "super_admin";
    public const string Admin = "admin";
    public const string SuperAdminPolicy = "SuperAdmin";

    public static string ToClaimValue(this AdminRole role) =>
        role == AdminRole.SuperAdmin ? SuperAdmin : Admin;
}
```

### 5.2 New: `API/Auth/ClaimsPrincipalExtensions.cs`

- `string GetUsername(this ClaimsPrincipal p)` returns `p.Identity!.Name!`.
- `bool IsSuperAdmin(this ClaimsPrincipal p)` returns `p.IsInRole(AdminRoles.SuperAdmin)`.

### 5.3 New: `API/Auth/UserScope.cs`

This is a pure helper, so it can be unit-tested directly:

```csharp
public static class UserScope
{
    public static bool IsVisibleTo(User user, string username, bool isSuperAdmin) =>
        isSuperAdmin || string.Equals(user.CreatedBy, username, StringComparison.OrdinalIgnoreCase);

    public static bool CanSee(this ClaimsPrincipal me, User user) =>
        IsVisibleTo(user, me.GetUsername(), me.IsSuperAdmin());
}
```

### 5.4 New: `API/Auth/AdminTokenValidator.cs`

This is the `OnTokenValidated` handler:

1. Resolve `AdminRepository` from `context.HttpContext.RequestServices`.
2. Load the admin by `context.Principal.Identity.Name`.
3. If it is `null` or `IsPending`, call `context.Fail("Admin no longer active")`. The client then gets **401**.
4. Otherwise remove every claim of type `identity.RoleClaimType` and add `new Claim(ClaimTypes.Role, admin.Role.ToClaimValue())`.

### 5.5 [TokenService.cs](management-app/backend/API/Auth/TokenService.cs)

- `CreateAccessToken(string username, AdminRole role)` emits `ClaimTypes.Name` **and** `ClaimTypes.Role`.
- The rest is unchanged.

### 5.6 [AdminRepository.cs](management-app/backend/API/Storage/AdminRepository.cs)

Every write does read → check → write **inside `_lock`**. Add these methods:

| Method | Behavior |
|--------|----------|
| `Task<Admin?> CreateFirstSuperAdminAsync(string username, string hash, string salt)` | Creates the super admin **only if the file holds no admins**. Otherwise returns `null`. This closes the race where two concurrent first logins would both become super admins. |
| `Task<bool> AddAsync(Admin admin)` | Inserts the admin. Returns `false` if the username exists (case-insensitive). |
| `Task<Admin?> SetInitialPasswordAsync(string username, string hash, string salt)` | Sets the password **only if the admin is still pending**. Otherwise returns `null`, because someone else claimed it first. |
| `Task<bool> AddRefreshTokenAsync(string username, RefreshTokenEntry entry, string? revokeToken)` | Prunes expired tokens, removes `revokeToken`, and adds `entry`. Returns `false` if the admin no longer exists. |
| `Task<bool> DeleteAsync(string username)` | Removes the admin. |
| `Task EnsureSuperAdminAsync()` | Migration: if admins exist and none is `SuperAdmin`, promotes `admins[0]`. Logs at `Information`. |

Also make these changes:

- **Remove** the upserting `UpdateAsync`, or make it non-upserting. Today login and refresh write back a stale full copy of the admin. Once delete exists, a login that runs at the same moment as a delete would **bring the deleted admin back**. Both endpoints switch to `AddRefreshTokenAsync`.
- Extract a private `SaveAllAsync(List<Admin>)` that writes the whole list, following the pattern in `UserRepository`. It writes **atomically**: first to `admins.json.tmp`, then `File.Move(tmp, _path, overwrite: true)`. This matters because `OnTokenValidated` now reads the file on every authenticated request. With `File.Create`, a reader can see a truncated or half-written file. *(As built: reads also take the repository lock — Windows refuses to replace a file that another handle has open, which the concurrent read/write unit test caught. The temp-file swap is kept for crash safety.)*

### 5.7 [UserRepository.cs](management-app/backend/API/Storage/UserRepository.cs)

- Add `Task<int> ReassignOwnerAsync(string fromUsername, string toUsername)`. It runs under the lock and returns the number of users it changed. It is used by the admin delete.

### 5.8 [Program.cs](management-app/backend/API/Program.cs)

- JSON: add the enum converter (§4.3).
- In `AddJwtBearer`, set `NameClaimType = ClaimTypes.Name` and `RoleClaimType = ClaimTypes.Role` explicitly, and add `o.Events = new JwtBearerEvents { OnTokenValidated = AdminTokenValidator.ValidateAsync }`.
- Replace `AddAuthorization()` with:
  ```csharp
  builder.Services.AddAuthorizationBuilder()
      .AddPolicy(AdminRoles.SuperAdminPolicy, p => p.RequireRole(AdminRoles.SuperAdmin));
  ```
- After `builder.Build()` and before mapping the endpoints, add `await app.Services.GetRequiredService<AdminRepository>().EnsureSuperAdminAsync();`.
- Add `api.MapAdminsEndpoints();`.

### 5.9 [AuthEndpoints.cs](management-app/backend/API/Endpoints/AuthEndpoints.cs): the new login flow

```text
if Username or Password is blank            -> 400
admin = GetByUsername(username)
if admin is null:
    admin = CreateFirstSuperAdminAsync(username, Hash(password))
    if admin is null                        -> 401   (admins exist, or lost the bootstrap race)
    log "created super admin"
else if admin.IsPending:
    claimed = SetInitialPasswordAsync(admin.Username, Hash(password))
    if claimed is null:                          (someone claimed it a moment earlier)
        admin = GetByUsername(username)
        if !Verify(admin, password)         -> 401
    else admin = claimed
    log "initial password set for {Username}"   (never log the password)
else if !Verify(admin, password)            -> 401

access = CreateAccessToken(admin.Username, admin.Role)
if !AddRefreshTokenAsync(admin.Username, newEntry, revokeToken: null) -> 401   (deleted at the same moment)
return 200 + refresh cookie
```

`/auth/refresh` changes:

- Reject an admin that is `IsPending` (401).
- Issue the token with the role.
- Use `AddRefreshTokenAsync(username, newEntry, revokeToken: oldToken)`.

### 5.10 New: `API/Endpoints/AdminsEndpoints.cs`

`builder.MapGroup("/admins").RequireAuthorization(AdminRoles.SuperAdminPolicy)`

| Route | Request | Responses |
|-------|---------|-----------|
| `GET /admins` | none | `200 AdminResponse[]`, ordered with the super admin first, then by username |
| `POST /admins` | `{ "username": "alice" }` | `201 AdminResponse` (status `pending`). `400` if the trimmed username fails `^[A-Za-z0-9._@-]{1,64}$`. `409` if it already exists (case-insensitive). |
| `DELETE /admins/{username}` | none | `204`. `404` if unknown. `400` if the target is the super admin. Before deleting, it calls `ReassignOwnerAsync(username, caller)`. |

DTO: the response **never** includes the hash, salt, or tokens.

```csharp
public record AdminResponse(string Username, AdminRole Role, AdminStatus Status, int UserCount);
public enum AdminStatus { Pending, Active }   // -> "pending" / "active"
public record CreateAdminRequest(string Username);
```

Each handler logs the action and the actor, for example `"POST /admins — {Actor} created admin {Username}"`.

### 5.11 [UsersEndpoints.cs](management-app/backend/API/Endpoints/UsersEndpoints.cs)

Every handler takes a `ClaimsPrincipal me` parameter.

- `GET /`: `users.Where(me.CanSee)`.
- `POST /`: set `CreatedBy = me.GetUsername()`.
- `PUT /{id}` and `DELETE /{id}`: after `GetByIdAsync`, `if (existing is null || !me.CanSee(existing)) return Results.NotFound();`. The check runs **before** any Xray call.
- `PUT` leaves `CreatedBy` untouched. The super admin editing an admin's user doesn't change who owns it.

### 5.12 [UsageEndpoints.cs](management-app/backend/API/Endpoints/UsageEndpoints.cs)

```csharp
var records = await usageRepo.GetAllAsync();
if (!me.IsSuperAdmin())
{
    var ownIds = (await userRepo.GetAllAsync()).Where(me.CanSee).Select(u => u.Id).ToHashSet();
    records = records.Where(r => ownIds.Contains(r.UserId)).ToList();
}
```

The super admin keeps seeing every record, including records of deleted users (today's behavior).

### 5.13 Left unchanged

These have nothing to do with which admin is calling, so they stay as they are:

- `ConfigEndpoints`: public and looked up by user ID.
- `QuotaEnforcerService`, `MetricsCollectorService`, `XrayConfigSyncService`, `XrayConfigFileService`: they work on all users, and `XrayService` only projects `Id`, `Secret` and `Name`.

---

## 6. Frontend changes

### 6.1 [types/index.ts](management-app/frontend/src/types/index.ts)

```ts
export type AdminRole = 'super_admin' | 'admin'
export interface Session { username: string; role: AdminRole }
export interface AdminAccount { username: string; role: AdminRole; status: 'pending' | 'active'; user_count: number }
// User: add
created_by?: string | null
```

### 6.2 [services/api.ts](management-app/frontend/src/services/api.ts)

- Replace `usernameFromAccessToken` with `sessionFromAccessToken(token): Session | null`. It decodes `name` with the existing rule (`key === 'name' || key.endsWith('/name')`) and `role` with `key === 'role' || key.endsWith('/role')`. If the role is missing it defaults to `'admin'`. `refreshAccessToken` uses `.username` from the result.
- Add `class ApiError extends Error { status: number }`. `request()` throws it, keeping the current `"${status} ${statusText}"` message so existing callers don't change. Pages can then tell `409` and `400` apart.
- Add `adminsApi`: `list()`, `create(username)`, `remove(username)`. The username is URL-encoded.

### 6.3 [hooks/useAuth.ts](management-app/frontend/src/hooks/useAuth.ts)

Keep a `session` state, initialized from the stored token. Return `{ isAuthenticated, session, isSuperAdmin, login, logout }`. The values are set on login and cleared on logout. The role can't change across a refresh, and a 401 already triggers a full reload.

### 6.4 New: `hooks/useAdmins.ts`

Same shape as `useUsers`: `{ admins, loading, error, create, remove, reload }`, including `useRefreshOnReconnect`.

### 6.5 New: `pages/AdminsPage.tsx` + `AdminsPage.module.css`

- Header: **Admins** with a **+ Add Admin** button.
- Form card **New Admin** with a single **Username** field, inside a `<label>` so e2e can use `getByLabel`. Hint text below it: *"No password is needed. The admin chooses one the first time they sign in. Share the username and ask them to sign in soon."*
- Error mapping:
  - `409` shows "An admin with this username already exists".
  - `400` shows "Use 1–64 letters, digits, or . _ @ -".
- Table columns:
  - **Username**
  - **Role**: "Super admin" / "Admin"
  - **Status**: badge "Pending first login" / "Active"
  - **Users**: `user_count`
  - **Actions**: **Delete**, hidden on the super admin row. It asks for confirmation: *"Delete admin X? Their N users will be reassigned to you."*
- Empty state when only the super admin exists: "No other admins yet."
- Styles: reuses `UsersPage.module.css` for the table, badge and form classes, plus a small `AdminsPage.module.css` (hint text, pending badge), so it looks the same without duplicating CSS.

### 6.6 [App.tsx](management-app/frontend/src/App.tsx)

- Get `session` and `isSuperAdmin` from `useAuth()`.
- Add a route: `/admins` renders `isSuperAdmin ? <AdminsPage /> : <Navigate to="/users" replace />`.
- Pass `isSuperAdmin` and `username` to `Layout`, and `showOwner={isSuperAdmin}` to `UsersPage`.

### 6.7 [components/Layout.tsx](management-app/frontend/src/components/Layout.tsx)

- Show the **Admins** `NavLink` only when `isSuperAdmin`.
- Add a small "signed in as" label (`{username}`, plus `· Super admin` for the super admin). Once there are several accounts, it shows which one you're using.

### 6.8 [pages/UsersPage.tsx](management-app/frontend/src/pages/UsersPage.tsx)

- New prop `showOwner: boolean`. When it is true, render an **Owner** column (`<td data-label="Owner">{u.created_by ?? '—'}</td>`).

### 6.9 [pages/LoginPage.tsx](management-app/frontend/src/pages/LoginPage.tsx)

- Add a hint under the subtitle: *"First time signing in? Use the username you were given and choose your password."* No other change: a pending admin signs in through the normal form.

### 6.10 Left unchanged

- **UsagePage / useUsage**: no change needed. The server already scopes `/users` and `/usage`, and the summaries are built from `users`.

### 6.11 Mock API ([frontend/mock](management-app/frontend/mock))

- `middleware.cjs`:
  - `createMockToken(username)` adds `role: username === 'admin' ? 'super_admin' : 'admin'`.
  - Intercept `POST /v1/admins` and rewrite `req.body` to `{ id: username, username, role: 'admin', status: 'pending', user_count: 0 }` before calling `next()`. The faker needs an `id`.
- `db.cjs`:
  - Add an `admins` collection: `admin` as super admin and active, `alice` active, `bob` pending. Each record's `id` equals its `username`.
  - Add `created_by` to the mock users (for example, Carol belongs to `alice`).
- The mock does **not** scope data per admin. Note that in a comment.

---

## 7. Backend tests (xUnit)

### 7.1 Test infrastructure

- [CustomWebApplicationFactory.cs](management-app/backend/API.Tests/Integration/CustomWebApplicationFactory.cs):
  - Expose `public string DataDir => _tempDir;`.
  - Add `public void SeedFile(string relativePath, string content)`. It creates the directories and writes the file. Call it **before** the first `CreateClient()`.
- New `Integration/TestAuth.cs` with these helpers:
  - `LoginAsync(client, user, pass) → string token`
  - `CreateClientAsAsync(factory, user, pass) → HttpClient` with the bearer header set
  - `CreateAdminAsync(superClient, username)`
  - `ReadClaim(jwt, claimType)` via `JwtSecurityTokenHandler().ReadJwtToken`. The package comes transitively from the API project.
- Refactor the duplicated `SetupAuth()` in the Users, Usage and Config tests to use `TestAuth`. This is cleanup only; the behavior stays the same.
- Existing tests all bootstrap as `admin` / `pass`. That account becomes the super admin and sees everything, so **the existing assertions keep passing unchanged**.

### 7.2 New unit tests

**`Unit/AdminRepositoryTests.cs`** uses a temp dir, following the pattern in `UsageRepositoryTests`:

- `CreateFirstSuperAdmin_EmptyStore_CreatesSuperAdmin`
- `CreateFirstSuperAdmin_AdminsExist_ReturnsNull`
- `CreateFirstSuperAdmin_ConcurrentCalls_ExactlyOneSucceeds`: 10 parallel calls with different usernames give exactly 1 non-null result and 1 admin in the file.
- `AddAsync_NewUsername_PersistsAsPendingAdmin`
- `AddAsync_DuplicateUsernameDifferentCase_ReturnsFalse`
- `SetInitialPassword_PendingAdmin_SetsHashAndSalt`
- `SetInitialPassword_ActiveAdmin_ReturnsNullAndKeepsOriginalHash`
- `SetInitialPassword_UnknownAdmin_ReturnsNull`
- `AddRefreshToken_DeletedAdmin_ReturnsFalseAndDoesNotRecreate`: this is the regression test for the upsert resurrection.
- `AddRefreshToken_RevokesGivenTokenAndPrunesExpired`
- `EnsureSuperAdmin_LegacyFileWithoutRole_PromotesFirstAdmin`: seed raw JSON with no `role` field.
- `EnsureSuperAdmin_SuperAdminExists_NoChange`
- `EnsureSuperAdmin_EmptyStore_NoOp`
- `Serialization_RoleWrittenAsSnakeCaseString_PendingHashWrittenAsNull`: read the raw file text.
- `Delete_RemovesAdmin`

**`Unit/TokenServiceTests.cs`** (there are no TokenService tests today):

- `CreateAccessToken_ContainsNameAndRoleClaims` (`[Theory]` for both roles)
- `HashThenVerify_CorrectPassword_True`
- `Verify_WrongPassword_False`
- `HashPassword_SamePasswordTwice_DifferentSalts`

**`Unit/UserScopeTests.cs`** (`[Theory]` / `[InlineData]`):

- Super admin sees an owned user, another admin's user, and a `null`-owner user.
- An admin sees their own user, including when the case differs.
- An admin does **not** see another admin's user or a `null`-owner user.

**`Unit/UserRepositoryTests.cs`** (new file):

- `ReassignOwner_MovesOnlyMatchingUsers_ReturnsCount`

### 7.3 Integration tests

**[AuthIntegrationTests.cs](management-app/backend/API.Tests/Integration/AuthIntegrationTests.cs)**: update these.

- `FirstLogin_NoAdmins_CreatesAdminAndReturnsTokens` → rename it to `…CreatesSuperAdmin…` and add an assertion that the role claim is `super_admin`.
- `FirstLogin_ConcurrentDifferentUsernames_OnlyOneSuperAdminCreated`: 5 parallel logins give exactly one 200.
- `Login_BlankPassword_Returns400`
- `Login_BlankUsername_Returns400`
- `PendingAdmin_FirstLogin_SetsPasswordAndReturnsAdminRoleToken`
- `PendingAdmin_SecondLoginSamePassword_Returns200`
- `PendingAdmin_SecondLoginDifferentPassword_Returns401`: the password can only be claimed once.
- `PendingAdmin_ConcurrentClaimsDifferentPasswords_ExactlyOneSucceeds`
- `Refresh_IssuesTokenWithRoleClaim`
- `LegacyAdminsFile_WithoutRole_FirstAdminIsPromotedToSuperAdmin`: seed `admins.json` with a real hash from `TokenService.HashPassword` and no `role`. Then log in and expect a `super_admin` claim and `GET /admins` = 200.
- `DeletedAdmin_ExistingAccessToken_Returns401`: create → claim → get a token → super admin deletes → `GET /users` returns 401.
- `DeletedAdmin_Refresh_Returns401`
- The existing tests stay as they are: wrong password, unknown user, refresh valid/invalid/unknown.

**New `Integration/AdminsIntegrationTests.cs`:**

- `CreateAdmin_AsSuperAdmin_Returns201Pending`
- `CreateAdmin_ResponseNeverContainsSecrets`: the response has no `password_hash`, `password_salt` or `refresh_tokens` properties.
- `CreateAdmin_Duplicate_Returns409` (including a duplicate that differs only in case)
- `CreateAdmin_InvalidUsername_Returns400` (`[Theory]`: `""`, `"   "`, `"a b"`, 65 characters, `"x/y"`)
- `CreateAdmin_CannotCreateUsernameOfSuperAdmin_Returns409`
- `ListAdmins_ShowsSuperAdminFirstWithStatusesAndUserCounts`
- `AdminsEndpoints_AsNormalAdmin_Return403`: GET, POST and DELETE.
- `AdminsEndpoints_Unauthenticated_Return401`
- `DeleteAdmin_ReassignsTheirUsersToSuperAdmin`
- `DeleteAdmin_SuperAdmin_Returns400`
- `DeleteAdmin_Unknown_Returns404`
- `DeleteAdmin_ThenLoginWithOldCredentials_Returns401`

**[UsersIntegrationTests.cs](management-app/backend/API.Tests/Integration/UsersIntegrationTests.cs)**: add these.

- `CreateUser_SetsCreatedByToCaller`
- `NormalAdmin_ListUsers_ReturnsOnlyOwnUsers`
- `SuperAdmin_ListUsers_ReturnsAllUsersIncludingOtherAdmins`
- `NormalAdmin_UpdateOtherAdminsUser_Returns404AndLeavesUserUnchanged`
- `NormalAdmin_DeleteOtherAdminsUser_Returns404AndUserStillExists`
- `NormalAdmin_UpdateOwnUser_Returns200`
- `SuperAdmin_UpdateAdminsUser_Returns200AndKeepsCreatedBy`
- `UpdateUser_BodyWithCreatedBy_IsIgnored`
- `LegacyUsersWithoutOwner_VisibleToSuperAdminOnly`: seed `users.json` with no `created_by` field.

**[UsageIntegrationTests.cs](management-app/backend/API.Tests/Integration/UsageIntegrationTests.cs)**: add these.

Seed `users.json` (users `u-alice`, owned by `alice`, and `u-bob`, owned by `bob`, both `enabled: false` so that no Xray calls are attempted). Seed `usage_history/<recent-date>.csv` with records for `u-alice`, `u-bob` and `u-deleted`.

- `NormalAdmin_GetUsage_ReturnsOnlyRecordsOfOwnUsers`
- `SuperAdmin_GetUsage_ReturnsAllRecordsIncludingDeletedUsers`
- `NormalAdmin_WithNoUsers_GetUsage_ReturnsEmpty`

**ConfigIntegrationTests:** no change. Optionally add `GetConfig_UserCreatedByNormalAdmin_IsPublic`.

---

## 8. E2E tests (Playwright)

### 8.1 Shared-state concerns (fix these first)

- The suite runs against **one persistent backend**. [users.spec.ts:20-34](management-app/e2e/tests/users.spec.ts#L20-L34) deletes **every** user as the super admin before each test. Playwright runs spec files in parallel workers, so that cleanup can delete users that other spec files have just created. This race already exists with `usage.spec.ts`, and the new specs would make it worse.
  - **Fix**: set `workers: 1` in [playwright.config.ts](management-app/e2e/playwright.config.ts).
- Created admins persist between runs. So:
  - generate unique usernames, and
  - delete every admin you create in `afterEach` / `afterAll` via `DELETE /admins/{username}`.
- [global-setup.ts](management-app/e2e/global-setup.ts) should also assert two things: that the login returned 200, and that the token's role claim is `super_admin`. If either fails, throw a clear error ("e2e backend must start with empty /data or with admin/password123 as super admin").

### 8.2 New: `e2e/tests/helpers.ts`

This removes the duplicated `loginAndNavigate` from the users and usage specs. It exports:

- `SUPER = { username: 'admin', password: 'password123' }`
- `apiLogin(request, username, password) → token`
- `openAs(page, creds, path)`: logs in through the API, sets `localStorage`, then navigates.
- `createAdmin(request, superToken, username)`
- `deleteAdmin(request, superToken, username)`
- `createUser(request, token, name)`
- `uniqueName(prefix)`, which returns `${prefix}-${Date.now()}-${random4}` and satisfies the username regex.

### 8.3 New: `e2e/tests/admins.spec.ts`

1. The super admin sees the **Admins** nav link and the "signed in as admin · Super admin" label.
2. The super admin creates an admin through the UI. The form has **no password field**, and the new row shows **Pending first login**.
3. Creating a duplicate username shows "already exists".
4. **First login sets the password**: a pending admin signs in through `/login` with a new password, lands on `/users`, and sees the empty state. The Admins link is **not** shown.
5. After the claim:
   - the same password works,
   - a different password shows "Invalid username or password",
   - the super admin's list shows **Active**.
6. A normal admin who goes to `/admins` is redirected to `/users`. `GET /api/v1/admins` with their token returns **403**.
7. When the super admin deletes an admin, the row disappears, the deleted admin can no longer sign in, and their users now appear under the super admin's ownership.

### 8.4 New: `e2e/tests/scoping.spec.ts`

Setup: create admins A and B, claim both, and create `A-user` as A and `B-user` as B. Teardown: delete the users and both admins.

1. On `/users`, admin A sees `A-user` and **not** `B-user`. B sees the opposite.
2. The super admin's `/users` shows both, with an **Owner** column holding A's and B's usernames.
3. A normal admin's `/users` has **no** Owner column.
4. On `/usage`, admin A's summary shows `A-user` only. The super admin's shows both. (E2E has no real traffic, so this checks the user cards, the same way the existing usage test does. Record-level filtering is covered by the integration tests in §7.3.)
5. A `PUT` and a `DELETE` by admin A on `B-user`'s ID both return **404**.

### 8.5 Updates to the existing specs

- `auth.spec.ts`:
  - "first login creates admin…" → also assert the **Admins** link is visible, since the bootstrap admin is the super admin.
  - Add "login page shows first-time sign-in hint".
- `users.spec.ts`:
  - switch to the helpers;
  - add "super admin sees Owner column".
- `usage.spec.ts`: switch to the helpers.

---

## 9. Documentation updates

| File | Change |
|------|--------|
| [README.md](README.md) line 41 | "first login creates the **super admin** account" |
| [docs/hosting.md](docs/hosting.md) §First Login | Explain the super admin. Add an **"Adding Admins"** section: Admins page → Add Admin → share the username → they set the password on first sign-in → they only see their own users. Add **"Recovering a lost password"**: set `password_hash` / `password_salt` to `null` in `/data/admins.json`, and the next login sets a new one. This works for the super admin too. |
| [docs/hosting.md](docs/hosting.md) §Data Storage | `admins.json`: roles, plus pending admins with no password. `users.json`: `created_by`. |
| [management-app/backend/README.md](management-app/backend/README.md) §1.3, §2.1 | Replace the outdated `admins.json` sample with the real schema (§4.1). Document `created_by`, and list `/admins` among the endpoints. |

---

## 10. Security checklist

- [ ] **Pending-account claim window.** Anyone who knows a pending username can set its password first. This is unavoidable with "the password is set at first login". It is documented in the UI hint and in hosting.md. The way to recover is to delete the admin and create it again. **Risk accepted.** A lost password can also be recovered by setting `password_hash` / `password_salt` to `null` in `admins.json` (documented in hosting.md).
- [ ] Login returns the same `401` for an unknown user and a wrong password. The blank-credential `400` is checked before the lookup.
- [ ] Bootstrap and claim are atomic under the repository lock. Concurrency tests cover both.
- [ ] `AdminResponse` never serializes the hash, salt or refresh tokens. A test asserts this.
- [ ] The stored role is authoritative (`OnTokenValidated`). Deleted or pending admins are rejected immediately.
- [ ] Accessing another admin's users returns 404, not 403. Ownership is checked before any Xray side effect.
- [ ] The super admin can't be deleted through the API, and `created_by` can't be changed through the API.
- [ ] Passwords are never logged. Admin create and delete are logged with the actor.
- [ ] `admins.json` is written atomically (temp file + move).

---

## 11. Implementation order

Each step leaves the build green. Run `dotnet test` after each backend step.

1. [x] **Models & storage**: `AdminRole`, nullable password fields, `IsPending`, `User.CreatedBy`, the enum converters, and the new `AdminRepository` methods with the atomic save. Add `UserRepository.ReassignOwnerAsync`. Then write the AdminRepository and UserRepository unit tests.
2. [x] **Tokens & auth plumbing**: the role claim in `TokenService`, `AdminRoles`, `ClaimsPrincipalExtensions`, `AdminTokenValidator`, the policy, `EnsureSuperAdminAsync` at startup. Then write the TokenService unit tests.
3. [x] **Login & refresh flow** (§5.9). Then update `AuthIntegrationTests` and the `CustomWebApplicationFactory` / `TestAuth` infrastructure.
4. [x] **Admins endpoints** (§5.10). Then write `AdminsIntegrationTests`.
5. [x] **Scoping**: `UserScope`, `UsersEndpoints`, `UsageEndpoints`. Then write the `UserScopeTests` and the new Users and Usage integration tests.
6. [x] **Frontend**: types → api.ts → useAuth → useAdmins → AdminsPage → App / Layout / UsersPage / LoginPage. Then run `pnpm lint && pnpm build`.
7. [x] **Mock API** (§6.11). Smoke-test with `pnpm dev`.
8. [x] **E2E**: config `workers: 1`, global-setup assertion, helpers, `admins.spec.ts`, `scoping.spec.ts`, and the updates to the existing specs.
9. [x] **Docs** (§9).

### Verification

- [ ] `dotnet test management-app/backend/OutlineManager.slnx`: all green, old and new tests.
- [ ] `cd management-app/frontend && pnpm lint && pnpm build`
- [ ] Start the app with an **empty `/data`** on port 8080, then run `cd management-app/e2e && pnpm test`. Run it a **second time without wiping data**, to show the suite cleans up after itself and is re-runnable.
- [ ] Upgrade path: start the new build against a copy of a pre-feature `/data`. The old admin logs in, is a super admin, and sees every existing user and all usage.
- [ ] Manual check:
  - the super admin creates `alice`;
  - `alice` signs in with a new password and creates a user;
  - the super admin sees that user with Owner = alice;
  - `alice` doesn't see the super admin's users;
  - the super admin deletes `alice` → alice's open session gets a 401 on its next request.

---

## 12. Decisions and remaining options

1. **Deleting an admin — accepted.** `DELETE /admins/{username}` is included and reassigns the deleted admin's users to the super admin.
2. **Password reset — not wanted.** No reset endpoint. Recovery is delete-and-recreate, or the manual `admins.json` edit documented in hosting.md.
3. **Pending-account claim window — risk accepted.** Pending expiry (an invitation that stops being claimable after N days) is not included.
4. **Usage page filter by admin for the super admin (not included).** The super admin already sees everything. A filter dropdown would be a convenience only.
5. **Frontend unit tests.** The frontend has no unit-test runner (no vitest), so the frontend logic is covered by e2e. Adding vitest for `sessionFromAccessToken` would be a separate, small task.

---

## 13. Noticed during review (out of scope, worth a separate fix)

- **Enable/Disable wipes the quota and expiry.** `useUsers.toggle` sends only `{ enabled }`, and [UsersEndpoints.cs:64-65](management-app/backend/API/Endpoints/UsersEndpoints.cs#L64-L65) assigns `Quota = req.Quota` and `Expiry = req.Expiry` unconditionally. Both become `null`.
- **`UserRepository` has the same non-atomic write.** It uses `File.Create` + serialize. This plan fixes it only for `admins.json`.
- **`API.http` is stale.** It still references `/weatherforecast`.

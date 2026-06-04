# Person-based access (PersonId → MenuId, PersonId → FeatureId)

## Database tables

| Table | Purpose |
|-------|---------|
| `PersonMenus` | `(PersonId, MenuId)` — which sidebar menus this person can see |
| `PersonFeatures` | `(PersonId, PermissionId)` — which features/API actions this person can use |

When admin grants a menu section, **both** tables are populated for that person (menu subtree + all linked features).

## Login flow

1. `POST /api/auth/login` — validates user, sets cookie, returns `session` with:
   - `sidebar` — only granted menus
   - `permissions` — only granted feature keys
   - `loginInstructions` — admin notes for this user

2. Or after login: `GET /api/auth/session` or `GET /api/auth/my-menus`

## Admin: grant access

```http
POST /api/rbac/persons/{personId}/grant-menu/{menuId}
{ "reason": "Accounts team" }
```

Also works via staff:

```http
POST /api/rbac/staff/{staffId}/grant-menu/{menuId}
```

View grants:

```http
GET /api/rbac/persons/{personId}/access
```

Revoke:

```http
POST /api/rbac/persons/{personId}/revoke-menu/{menuId}
```

## Resolution order (regular user)

1. If rows exist in `PersonMenus` / `PersonFeatures` → **use only those** (simple model)
2. Else if user has `StaffVacancy` → legacy RBAC (matrix, groups, overrides)
3. Else → empty sidebar (no access until admin grants)

SuperAdmin / Admin → full access always.

## Setup

```http
POST /api/rbac/seed-features
POST /api/menus/seed
POST /api/rbac/link-menus-to-features
```

Apply migration: `dotnet ef database update`

## Frontend

- After login, use `response.session.sidebar` and `response.session.permissions`
- Or call `GET /api/auth/my-menus` with credentials
- Hide routes/buttons unless `permissions.includes('FEATURE_KEY')`
- Admin grant UI: pick person from `GET /api/rbac/users`, then `POST grant-menu` with `personId`

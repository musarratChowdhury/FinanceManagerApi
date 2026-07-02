# Current Needs Feature Plan

A wishlist/shopping list of items the user might buy later. Items are saved with estimated price, reason, entry date, possible buying date, and deadline. The list always shows the total money needed.

## Decisions

- **Purchased/abandoned items**: delete-only; no status flag, no history. Total = sum of all current rows.
- **Pricing**: single `ApproxPrice` per item; no quantity field.
- **User scoping**: list shows only the logged-in user's items, filtered by `CreatedBy`.

## Domain Model — `CurrentNeed`

| Field | Type | Notes |
|---|---|---|
| `Id` | `long` | PK, `IdentityByDefaultColumn` |
| `Name` | `string` | Required — what to buy |
| `ApproxPrice` | `double` | Required — estimated price |
| `Reason` | `string?` | Nullable |
| `EntryDate` | `DateTime` | Default `DateTime.UtcNow` |
| `PossibleBuyingDate` | `DateTime?` | Nullable |
| `Deadline` | `DateTime?` | Nullable |
| `CreatedBy` | `Guid` | From JWT; used for per-user scoping |

**Total = `sum(ApproxPrice)` for the current user's rows.**

## API Implementation — `FinanceManagerApi`

1. **`Models/Entity/CurrentNeed.cs`**
   - Add auto-properties for the fields above (avoid the `Receipt.Expenses` field-as-collection anti-pattern).

2. **`Models/DTO/CurrentNeedDto.cs`**
   - Mirror entity fields.

3. **`Profiles/CurrentNeedProfile.cs`**
   - `CreateMap<CurrentNeed, CurrentNeedDto>().ReverseMap()`.
   - Add `.ForMember(e => e.Id, opt => opt.Ignore())` on the reverse map to prevent the client from injecting a PK on create.

4. **`Services/CurrentNeedService.cs` + `ICurrentNeedService`**
   - `GetAllAsync(Guid userId)`
   - `GetByIdAsync(long id)`
   - `CreateCurrentNeedAsync(CurrentNeedDto dto, Guid userId)`
   - `UpdateCurrentNeedAsync(long id, CurrentNeedDto dto, Guid userId)`
   - `DeleteCurrentNeedAsync(long id, Guid userId)`
   - `GetTotalAsync(Guid userId)`
   - Use existing `GenericRepository.Filter(predicate)` for user scoping; no repository changes needed.

5. **`Controllers/CurrentNeedController.cs`**
   - `[ApiController]`, `[Authorize]`, `[Route("api/[controller]")]`.
   - Read `userId` from JWT (same pattern as `ExpenseCategoryController`).
   - Endpoints:
     - `GET /api/CurrentNeed`
     - `GET /api/CurrentNeed/{id}`
     - `POST /api/CurrentNeed`
     - `PUT /api/CurrentNeed/{id}`
     - `DELETE /api/CurrentNeed/{id}`
     - `GET /api/CurrentNeed/total`
   - Verify ownership (`CreatedBy == userId`) on update/delete.

6. **`DbContext/AppDbContext.cs`**
   - Add `public DbSet<CurrentNeed> CurrentNeeds { get; set; }`.
   - No `OnModelCreating` configuration needed.

7. **`Program.cs`**
   - Register `builder.Services.AddScoped<ICurrentNeedService, CurrentNeedService>();` alongside the other service registrations.
   - Add EF migration: `dotnet ef migrations add AddCurrentNeeds --project FinanceManagerApi`.
   - Apply locally with `db-fresh.ps1`; for prod, apply the generated idempotent SQL script against Cockroach Cloud.

## Angular Implementation — `financemanagerAngular17`

The route (`app.routes.ts`), nav link (`navbar.component.ts`), and stub component already exist for `/current-needs`.

1. **`src/models/CurrentNeed.ts`**
   - Mirror `Expense.ts` style: public typed fields, `id: number = 0` for new items, no client-side counter.

2. **`src/app/services/current-needs.service.ts`**
   - Mirror `expense-category.service.ts` (bare URLs: `POST /api/CurrentNeed`, etc.).
   - Add `getTotal(): Observable<number>` calling `GET /api/CurrentNeed/total`.
   - Fix the `handleError` bug while copying (use `throwError` instead of calling `error(...)` as a function).

3. **`src/app/pages/layout/current-needs/current-needs.component.{ts,html}`**
   - Flesh out the stub into a single page:
     - daisyUI `card` add-form: `ejs-textbox` (Name, Reason), `ejs-numerictextbox` (ApproxPrice), `ejs-datetimepicker` (PossibleBuyingDate, Deadline).
     - daisyUI `stats` banner showing the live **total** (fetched via `getTotal()`, refreshed after each add/delete).
     - `<ejs-grid>` listing items: Name, ApproxPrice (`format="C2"`), Reason, PossibleBuyingDate, Deadline, + Delete action.
     - Re-fetch list + total after each mutation.

## Build / Migration Order

1. API: entity → DTO → profile → service → controller → DbContext → `Program.cs` DI → build → add EF migration → apply locally.
2. Angular: model → service → component → `npm run build`.
3. Smoke test: add a few needs, verify total, delete one, verify total updates.

## Notes

- The Angular `CurrentNeed` model must send `id: 0` for new items so the server/DB assigns the real PK.
- The AutoMapper profile's `Ignore` on `Id` is defense-in-depth; even a misbehaving client cannot force a duplicate PK.
- Do not forget to register `ICurrentNeedService` in `Program.cs` (the `IncomeService` is currently unregistered, which is a known pitfall in this repo).

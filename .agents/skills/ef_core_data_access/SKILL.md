---
name: "EF Core SQLite Data Access"
description: "Triggers when modifying database schemas, writing queries, Repositories, or DbContext logic"
---

# EF Core & SQLite Data Access Standards

## Purpose
Ensure thread-safe, efficient database interactions in a Blazor Hybrid application using Entity Framework Core and SQLite.

## When to use
*   Creating or updating Repositories in `EchidnaJav.Core.Infrastructure.Persistence`.
*   Modifying `AppDbContext` or Entity Models.
*   Writing LINQ queries for SQLite.

## Relevant Folders
*   `EchidnaJav.Core/Infrastructure/Persistence/`
*   `EchidnaJav.Core/Migrations/`

## Project-Specific Conventions
1.  **DbContextFactory:** ALWAYS inject `IDbContextFactory<AppDbContext>` in repositories instead of injecting `AppDbContext` directly. This prevents scope-related concurrency exceptions in Blazor's async environment.
2.  **Using blocks:** Wrap DbContext instantiation in `using var db = _dbFactory.CreateDbContext();`.
3.  **No Tracking for Reads:** Use `.AsNoTracking()` for read-only queries (e.g., fetching lists for the UI) to reduce memory overhead.
4.  **Async/Await:** Use `ToListAsync()`, `FirstOrDefaultAsync()`, etc., for all DB I/O.

## Common Pitfalls
*   **Concurrency Exceptions:** Attempting to run parallel queries on a single, shared `AppDbContext` instance.
*   **Missing Migrations:** Forgetting to run `dotnet ef migrations add` after modifying models. The app runs `db.Database.Migrate()` on startup, so the migration file MUST exist.

## Validation Steps
*   Ensure the repository method uses a fresh DbContext from the factory.
*   Ensure all database calls are properly awaited.

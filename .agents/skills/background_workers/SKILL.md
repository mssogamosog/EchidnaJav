---
name: "Background Workers and Queues"
description: "Triggers when modifying IHostedService implementations, background tasks, or Job Queues"
---

# Background Worker & Queue Standards

## Purpose
Ensure long-running tasks (like mass scraping or importing) execute safely in the background without blocking the MAUI UI thread.

## When to use
*   Modifying `ActressScraperWorker` or `MovieScraperWorker`.
*   Updating `IActressScrapeQueue` or `IMovieScrapeQueue`.

## Relevant Folders
*   `EchidnaJav.Scraper/Services/`
*   `MauiProgram.cs` (Worker Booting Region)

## Project-Specific Conventions
1.  **IHostedService:** Background tasks implement `BackgroundService` or `IHostedService`.
2.  **Thread Safety:** The workers are booted manually in `MauiProgram.cs` via `Task.Run(async () => { ... })` to guarantee they don't block the MAUI startup lifecycle. Preserve this pattern.
3.  **Queues:** Jobs are pushed to Singleton Queues (`IMovieScrapeQueue`). The worker monitors the queue asynchronously (`WaitToAsync` or `DequeueAsync`).
4.  **Scope Management:** Because workers run as Singletons essentially, if they need to access a Scoped service (like `IDbContextFactory`), they must inject `IServiceProvider` and create a local scope: `using var scope = _services.CreateScope();`.

## Common Pitfalls
*   **UI Thread Blocking:** Running heavy synchronous operations in the worker that inadvertently lock the UI.
*   **Scoped Services in Singletons:** Attempting to inject a `Scoped` service directly into the `BackgroundService` constructor.

## Validation Steps
*   Ensure local scopes (`CreateScope()`) are used to resolve DB or Repository dependencies inside the worker's execution loop.
*   Verify cancellation tokens are passed down throughout the entire task chain.

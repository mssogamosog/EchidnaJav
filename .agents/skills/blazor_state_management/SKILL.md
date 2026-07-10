---
name: "Blazor State Management"
description: "Triggers when managing UI state, passing data between components, or modifying Singleton state classes"
---

# Blazor State Management Standards

## Purpose
Maintain consistent and predictable UI state across Razor components using centralized Singleton state services.

## When to use
*   Implementing search filters, navigation caching, or tracking background task progress in the UI.
*   Modifying classes in `EchidnaJav.Core.Domain.States`.
*   Injecting state into `.razor` files.

## Relevant Folders
*   `EchidnaJav.Core/Domain/States/`
*   `EchidnaJav/Components/`

## Project-Specific Conventions
1.  **Singleton State Classes:** Global state (e.g., `SearchFilterState`, `ImportState`) is registered as `AddSingleton` in `MauiProgram.cs`.
2.  **Event Notifications:** State classes should ideally implement event actions (e.g., `public event Action OnStateChanged;`) that components can subscribe to in `OnInitialized` and unsubscribe from in `Dispose`.
3.  **Triggering UI Updates:** Components consuming the state must call `InvokeAsync(StateHasChanged)` when the state's change event fires.

## Common Pitfalls
*   **Memory Leaks:** Forgetting to unsubscribe from State events in a component's `Dispose()` method.
*   **Thread Safety:** Updating State properties from a background thread without dispatching to the UI thread (use `InvokeAsync`).

## Validation Steps
*   Verify the State class is registered as a Singleton in `MauiProgram.cs`.
*   Check that the Razor component implements `IDisposable` and unsubscribes from state events.

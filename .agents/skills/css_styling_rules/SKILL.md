---
name: "CSS Styling Standards"
description: "Triggers when modifying, creating, or refactoring CSS styles or component layouts (e.g., .razor, .css, or html files)"
---

# CSS Styling and Component Layout Standards

To maintain clean, maintainable, and scoped styling in this project, follow these standards strictly.

## 1. No Inline Styles
*   **Do NOT** use inline `style="..."` attributes on HTML/Blazor markup.
*   Avoid inline styles for structural spacing, padding, margins, or colors.
*   Exceptions are permitted *only* for dynamic properties calculated at runtime (e.g., progress bar percentages or absolute positioning based on mouse coordinates) that cannot be handled via CSS classes or variables.

## 2. Follow Scoped CSS Component Structure
*   For Blazor pages or components (e.g., `MyPage.razor`), define styles in a matching scoped CSS file (e.g., `MyPage.razor.css`) in the same directory.
*   Organize selectors clean and flat. Leverage Blazor's automatic CSS isolation (which automatically scopes rules using `::deep` or standard selectors).

## 3. Use Global Styles Appropriately
*   Place global resets, custom properties/variables, utility classes, or shared base elements in the project's global stylesheet (e.g., `wwwroot/app.css`).
*   Check existing utility classes and variables before writing new custom rules to avoid redundancy.

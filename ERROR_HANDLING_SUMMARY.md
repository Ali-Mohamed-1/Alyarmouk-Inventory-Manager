# Error Handling & Message UI – Summary

## 1. Where Exceptions Are Handled

### Backend (global)

- **`src/Inventory.Web/Middleware/ExceptionHandlingMiddleware.cs`**
  - Registered early in `Program.cs` so all requests pass through it.
  - Catches:
    - **ValidationException** → HTTP 400, `userMessage` = `ex.Message`
    - **NotFoundException** → HTTP 404, `userMessage` = `ex.Message`
    - **ConflictException** → HTTP 409, `userMessage` = `ex.Message`
    - Any other exception → HTTP 500, generic message (no stack trace or raw exception text).
  - **API requests** (path starts with `/api`): responds with JSON `{ "userMessage": "..." }` and the appropriate status code.
  - **Non-API requests**: redirects to `/Home/Error?message=<userMessage>`.
  - Exception messages are **not** rewritten; the middleware uses the existing exception message as-is for `userMessage`.
  - Stack traces and raw exception text are **not** exposed to the client; they are only logged server-side.

### Error page (non-API)

- **`Controllers/HomeController.cs`** – `Error` action accepts `[FromQuery] string? message` and passes it to the view.
- **`Models/ErrorViewModel.cs`** – `UserMessage` property holds the message.
- **`Views/Shared/Error.cshtml`** – Displays `Model.UserMessage` when present; otherwise shows the generic “An error occurred…” text.

---

## 2. Confirmation: Browser Alerts Removed

- **All `alert()` usages** in the following have been removed or replaced with the shared toast message UI (`window.appMessages.showError` / `showSuccess`), with guards for `window.appMessages` where needed:
  - **Views/Customers/Index.cshtml**
  - **Views/SalesOrders/Details.cshtml**
  - **Views/PurchaseOrders/Details.cshtml**
  - **Views/Activity/Index.cshtml**
  - **Views/Dashboard/Index.cshtml**
  - **Views/Suppliers/Index.cshtml**
  - **Views/Financial/Index.cshtml**
  - **wwwroot/js/dashboard-operations.js**
  - **wwwroot/js/inventory.js**
- **`wwwroot/js/site.js`** – Contains no `alert()`; it only defines the shared message helpers and a comment that no browser alert popups are used.
- **Result:** No “localhost:5115 says…” (or similar) native browser alert popups remain from the application code. Errors and success messages are shown via the same toast component.

---

## 3. UI Locations Where Errors (and Success) Are Displayed

| Location | Context | How |
|--------|----------|-----|
| **Layout (global)** | Toast container | `#appToastContainer` in `Views/Shared/_Layout.cshtml`; all errors/success from `appMessages` show here as Bootstrap toasts (bottom-right). |
| **Error page** | Non-API failures | `/Home/Error` – full-page view showing `UserMessage` (or generic text) for non-API requests that throw. |
| **Customers/Index** | Refund, payment, due date, registration, upload, delete, cancel, sync, etc. | Toast via `appMessages.showError` / `showSuccess`; API failures use `getApiErrorMessage(response, errText)` so backend `userMessage` is shown. |
| **SalesOrders/Details** | Refund modal, payment info save, submit payment, due date | Toast; API errors use `getApiErrorMessage` for `userMessage`. |
| **PurchaseOrders/Details** | Refund, payment info, submit payment, payment deadline | Toast; API errors use `getApiErrorMessage`. |
| **Activity/Index** | Upload, delete, details load, status, payment, sync, cancel, refund (SO/PO), update, render details | Toast; API errors use `getApiErrorMessage` where fetch is used. |
| **Dashboard/Index** | Save failed | Toast. |
| **Suppliers/Index** | Load history, load failed, cancel, upload, delete, update, sync, payment, deadline, save, refund, apiAction | Toast; API errors use `getApiErrorMessage`. |
| **Financial/Index** | Date validation, expense add success/failure | Toast; expense API errors use `getApiErrorMessage`. |
| **dashboard-operations.js** | Supplier/product validation, complete receipt failure | Toast; receipt API errors use `getApiErrorMessage`. |
| **inventory.js** | Refund, order/customer/product validation, receive/issue/adjust success and errors, demo messages | Toast (all `alert()` replaced with `appMessages`). |

Errors are shown **near the action** in practice: the toast appears in a fixed position (bottom-right) immediately after the action; for modal flows (e.g. refund, payment), the modal can stay open while the toast shows the backend message. No business logic, exception types, or throw sites were changed; only presentation and mapping of existing exception messages to this UI.

---

## 4. Success Messages

- Existing success messages (e.g. “Refund processed successfully”, “Expense added!”, “Successfully received/issued/adjusted…”) are routed through **`window.appMessages.showSuccess(...)`** so they use the same toast component and **do not** trigger browser alerts.
- No new success wording was added; only the delivery mechanism was switched from `alert()` to toast.

---

## 5. Verification Checklist

| Item | Status |
|------|--------|
| No browser alert popups remain | Done – all `alert()` replaced or removed (except comment in site.js). |
| No “localhost:xxxx says” anywhere | Done – no `alert()` used. |
| Existing exception messages visible to user | Done – middleware sends `userMessage` from exception; frontend uses `getApiErrorMessage` for API responses. |
| Messages in correct UI context | Done – toasts used for all these flows; error page for non-API. |
| No business logic changed | Done – only presentation and error-mapping. |
| All tests still pass | Recommended to run `dotnet test` after closing other processes that lock the solution (build was blocked by file locks in this session). |

---

## 6. Optional Next Steps

- **Modal/inline errors:** To show errors *inside* the modal (e.g. refund error in refund modal) in addition to the toast, add a small error `<div>` in each modal and set its text from the same `userMessage` before or instead of calling `showError` for that action.
- **Identity/Login:** If any scaffolded Identity pages still use `alert()`, they can be updated to use `appMessages` in the same way once the layout includes the toast container and site.js.

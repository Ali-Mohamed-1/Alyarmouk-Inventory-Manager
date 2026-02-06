# Alyarmouk Inventory Manager – QA Audit Report

**Audit Date:** February 6, 2025  
**Scope:** Full lifecycle testing of Sales Orders, Purchase Orders, Refunds, Cancellations, Notifications, Stock Operations, and UI visibility  
**Method:** Code review + existing unit test analysis (56 tests executed, all passed)

---

## Executive Summary

The codebase implements robust backend rules for payment, refund, and cancellation flows. Unit tests cover the critical paths. **3 bugs** and **multiple test gaps** were identified. Backend invariants are well enforced; UI and notification logic have specific deviations from the specification.

---

## 1. Sales Order – Full Lifecycle Testing

### 1.1 Creation

| Flow | Status | Notes |
|------|--------|-------|
| Create with no payment | ✅ PASS | `PaymentStatus.Pending` at creation; no PaymentRecord |
| Create with partial payment | ⚠️ GAP | No UI path for "partial payment at creation"; must add via AddPayment after creation |
| Create with full payment | ✅ PASS | `PaymentStatus.Paid`; PaymentRecord created; `PaymentInvariantTests` covers |
| Order status correct | ✅ PASS | Pending by default |
| Payment status derived | ✅ PASS | `RecalculatePaymentStatus()`; domain invariant enforced |
| Net paid / remaining / deadline | ✅ PASS | `GetPaidAmount()`, `GetRemainingAmount()`, `DueDate`; DTOs expose correctly |

### 1.2 Stock Effects

| Flow | Status | Notes |
|------|--------|-------|
| Reserve stock on create (Pending) | ✅ PASS | `ReserveSalesOrderStockAsync` called after commit |
| OnHand / Reserved / Available on product & batch | ✅ PASS | `InventoryServices.ReserveSalesOrderStockAsync` updates `ProductBatch.Reserved` |
| Process stock on Done | ✅ PASS | `ProcessSalesOrderStockAsync` issues stock, releases reservation |

### 1.3 Payment Methods

| Method | Status | Notes |
|--------|--------|-------|
| Cash | ✅ PASS | No extra required fields |
| Bank Transfer | ✅ PASS | TransferId required when Paid at creation |
| Check | ✅ PASS | CheckReceived/CheckReceivedDate, CheckCashed/CheckCashedDate validated |
| Overpayment blocked | ✅ PASS | Domain throws `InvalidOperationException`; AddPayment rejects amount > remaining |

---

## 2. Refund Logic (Sales)

| Flow | Status | Notes |
|------|--------|-------|
| Full money refund | ✅ PASS | `RefundTests.RefundSalesOrder_FullInternalRefund_UpdatesTotalsAndServices` |
| Partial money refund | ✅ PASS | `RefundTests.RefundSalesOrder_PartialProductRefund_UpdatesLineAndStock` |
| Multiple partial refunds | ✅ PASS | RefundedAmount cumulates; cap enforced |
| Net paid / payment status recalc | ✅ PASS | `RecalculatePaymentStatus`; downgrades Paid→PartiallyPaid |
| Refund history | ✅ PASS | PaymentRecord (Refund) + RefundTransaction |
| Cannot refund money if not Paid | ✅ PASS | `order.PaymentStatus != PaymentStatus.Paid` → ValidationException |
| Full batch refund | ✅ PASS | RefundTests cover |
| Partial batch refund | ✅ PASS | RefundTests cover |
| Cannot refund stock unless Done | ✅ PASS | `order.Status != SalesOrderStatus.Done` → ValidationException |

---

## 3. Cancellation Logic (Sales)

| Flow | Status | Notes |
|------|--------|-------|
| Block when NetPaid ≠ 0 | ✅ PASS | `CancellationRulesTests.CannotCancelSalesOrder_WithPaidAmount` |
| Block when remaining stock ≠ 0 (Done) | ✅ PASS | `CancellationRulesTests.CannotCancelSalesOrder_WithRemainingStock` |
| Block when partially refunded (money) | ✅ PASS | Covered by NetPaid check |
| Block when partially refunded (stock) | ✅ PASS | RemainingStockQuantity check |
| Valid cancellation after full refund | ✅ PASS | `CancellationRulesTests.CanCancelSalesOrder_WhenFullyRefundedStockAndMoney` |
| Status → Cancelled | ✅ PASS | |
| No stock/money changes after cancel | ✅ PASS | Cancel only sets status; releases reservations if Pending |
| Cancelled cannot be edited/refunded | ✅ PASS | `CancellationRulesTests.CancelledSalesOrder_CannotReceivePaymentsOrRefunds` |
| Direct status change to Cancelled blocked | ✅ PASS | `UpdateStatusAsync` throws; must use `CancelAsync` |

---

## 4. Deadline & Notification Logic

| Flow | Status | Notes |
|------|--------|-------|
| Orders > 7 days → not shown | ✅ PASS | Filter: `DueDate <= upcomingWindow` (now+7) |
| Exactly 7 days left → shown | ✅ PASS | Included in window |
| < 7 days left → shown | ✅ PASS | Included |
| **Overdue orders → shown** | ❌ **FAIL** | **Spec: "Orders with < 7 days left → shown" includes overdue. Current filter: `DueDate >= now` excludes overdue.** |
| Paid orders removed | ✅ PASS | `remainingAmount <= 0` → skip |
| Cancelled never shown | ✅ PASS | `Status != Cancelled` |
| Remaining money owed correct | ✅ PASS | Net paid from ledger |

**Reproduction (Notification Overdue Bug):**
1. Create Sales Order with DueDate = yesterday, unpaid.
2. Call `GetActiveNotificationsAsync()`.
3. **Expected:** Order appears with negative DaysUntilDue (overdue).
4. **Actual:** Order does not appear (filter excludes DueDate < now).

**Fix:** Extend filter to include overdue: e.g. `DueDate <= upcomingWindow` (remove `DueDate >= now`) and compute `DaysUntilDue` as `(DueDate - now).Days` (can be negative).

---

## 5. Purchase Order – Full Lifecycle

| Flow | Status | Notes |
|------|--------|-------|
| Creation / no payment | ✅ PASS | |
| Add payment | ✅ PASS | `PurchaseOrder_AddPayment_Partial_SetsStatusToPartiallyPaid` |
| Stock increase on Received | ✅ PASS | `ProcessPurchaseOrderStockAsync` creates Receive transactions |
| Refund & cancellation rules | ✅ PASS | Mirror Sales; `CancellationRulesTests` cover |
| Supplier balance | ✅ PASS | `ReportingServices.GetSupplierBalanceAsync` |

---

## 6. Stock Operations

| Flow | Status | Notes |
|------|--------|-------|
| Issue from single batch | ✅ PASS | `InventoryTransactionServices` Issue type |
| Issue from multiple batches | ⚠️ GAP | No explicit test; service supports per-batch via `ProductBatchId` |
| Stock deduction only, no money | ✅ PASS | Issue does not create financial records |
| Adjust stock (increase/decrease/zero) | ✅ PASS | Adjust type supported |
| Audit trail | ✅ PASS | `InventoryTransaction` + `IAuditLogWriter` |

---

## 7. Product & Batch Integrity

| Flow | Status | Notes |
|------|--------|-------|
| Add product | ✅ PASS | Standard CRUD |
| Edit product | ✅ PASS | |
| Delete product constraints | ⚠️ GAP | Not explicitly tested |
| Add batch | ✅ PASS | Via receive/PO |
| Product onhand/reserved/available vs batch totals | ✅ PASS | `ProductBatchManagementTests`, `ProductBatchAvailabilityTests` |

---

## 8. Supplier / Customer Modules

| Flow | Status | Notes |
|------|--------|-------|
| Create/Edit supplier | ✅ PASS | |
| Supplier balance | ✅ PASS | Reporting services |
| Create/Edit customer | ✅ PASS | |
| Customer balance | ✅ PASS | |
| Refund impact on balances | ✅ PASS | Via PaymentRecord / financial ledger |

---

## 9. Activity Tab

| Flow | Status | Notes |
|------|--------|-------|
| Sales activity visibility | ✅ PASS | API + Activity view |
| Purchase activity visibility | ✅ PASS | |
| Issue / Adjust stock visibility | ✅ PASS | Via inventory transactions |
| Cancelled actions visible historically | ✅ PASS | Orders remain in list with status |

---

## 10. Order Details View

| Flow | Status | Notes |
|------|--------|-------|
| Full data visibility | ✅ PASS | GetByIdAsync returns complete DTO |
| Cancel option not in status editor | ✅ PASS | Status dropdown excludes Cancelled; Cancel is separate action |
| Refund / Cancel buttons respect rules | ✅ PASS | Activity view: Refund only when Done/Received; Cancel disabled when preconditions not met |

---

## 11. Cross-View Button Visibility Audit

| View | Refund Button | Cancel Button | Notes |
|------|---------------|---------------|-------|
| Sales Order Details | ❌ **MISSING** | ✅ | Refund only in Activity + Customer history |
| Purchase Order Details | ❌ **MISSING** | ✅ | Same |
| Activity (SO detail) | ✅ (when Done) | ✅ (when eligible) | Correct |
| Activity (PO detail) | ✅ (when Received) | ✅ (when eligible) | Correct |
| Customer history | ✅ (when Done or Paid) | ✅ | Conditional |

**Recommendation:** Add Refund button to Sales Order Details and Purchase Order Details when backend allows (order Done/Received for stock; Paid for money). Improves discoverability.

---

## 12. Financial Summary

| Flow | Status | Notes |
|------|--------|-------|
| Period selection | ✅ PASS | Financial/Reports |
| Reconciliation with transactions | ⚠️ GAP | Not explicitly tested |
| Internal expenses | ✅ PASS | Financial module |

---

## 13. UI Bugs

### Bug 1: Sales Order Create – Invalid PaymentStatus Option

**File:** `src/Inventory.Web/Views/SalesOrders/Create.cshtml`  
**Line:** 88

```html
<option value="2">OVERDUE</option>
```

**Issue:** PaymentStatus enum: 0=Pending, 1=Paid, 2=PartiallyPaid. "OVERDUE" is not a payment status; it is derived from DueDate and PaymentStatus. Selecting value 2 creates an order as PartiallyPaid (or effectively Pending if no payment), with a misleading label.

**Expected:** Only PENDING (0) and PAID UPON ISSUANCE (1). Remove OVERDUE or replace with PARTIAL only if partial-at-creation is supported (it is not).

**Fix:** Remove the `<option value="2">OVERDUE</option>` line.

---

### Bug 2: Notification – Overdue Orders Excluded

**File:** `src/Inventory.Infrastructure/Services/NotificationService.cs`  
**Lines:** 38–39 (Sales), 88–90 (Purchase)

**Issue:** Filter uses `DueDate >= now`, which excludes overdue orders. Spec requires overdue orders to be shown.

**Fix:** Include overdue in window:

```csharp
// Sales: show if DueDate within past+future 7 days (or similar window)
.Where(o => o.Status != SalesOrderStatus.Cancelled &&
            o.DueDate <= upcomingWindow)  // Remove: o.DueDate >= now
```

Ensure `DaysUntilDue` can be negative for overdue.

---

### Bug 3: Refund Button on Order Details View

**Files:** `Views/SalesOrders/Details.cshtml`, `Views/PurchaseOrders/Details.cshtml`

**Issue:** Refund action is not exposed on the Order Details page. Users must go to Activity or Customer/Supplier history. Spec calls for refund button visibility when allowed on every order-related view.

**Fix:** Add Refund button (same rules as Activity: Done/Received for stock, Paid for money) with modal or link to refund flow.

---

## 14. Regression & Cleanup Verification

| Check | Status | Notes |
|-------|--------|-------|
| Old refund logic unreachable | ✅ PASS | Single `RefundAsync` path; no legacy branches found |
| Old cancel logic unreachable | ✅ PASS | Single `CancelAsync`; status editor blocks direct Cancelled |
| No duplicate calculations | ✅ PASS | Single source: Payments ledger → RecalculatePaymentStatus |
| No double side effects | ✅ PASS | Transactions wrap operations |

---

## 15. Test Gap Summary

| Area | Gap |
|------|-----|
| Notification | No test for overdue orders in notifications |
| Refund | Explicit test for "Cannot refund money when PaymentStatus != Paid" |
| Refund | Explicit test for "Cannot refund stock when order not Done" |
| Create | No test for PaymentStatus=PartiallyPaid at creation (edge case) |
| Stock | Issue from multiple batches not explicitly tested |
| UI | No automated UI tests for button visibility |

---

## 16. Recommendations

1. **Fix Bug 1:** Remove "OVERDUE" from Sales Order Create PaymentStatus dropdown.
2. **Fix Bug 2:** Extend NotificationService to include overdue orders.
3. **Fix Bug 3:** Add Refund button to Sales Order Details and Purchase Order Details when allowed.
4. **Add unit tests:**
   - `NotificationServiceTests.SalesOrder_Overdue_IsReturned`
   - `RefundTests.RefundMoney_WhenNotPaid_Throws`
   - `RefundTests.RefundStock_WhenNotDone_Throws`
5. **Consider integration tests** for full user flows (create → pay → refund → cancel).
6. **Add UI automation** for critical button visibility (e.g., Playwright/Selenium) if budget allows.

---

## Appendix: Test Execution

```
Passed!  - Failed: 0, Passed: 56, Skipped: 0, Total: 56
```

All existing unit tests pass. No regressions detected in backend logic.

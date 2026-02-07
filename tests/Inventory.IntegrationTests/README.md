# Inventory Integration Tests

End-to-end integration tests for the Alyarmouk Inventory Manager that validate complete business behavior across **Sales Orders, Purchase Orders, Stock, Payments, Refunds, Cancellation, Notifications, Reporting, and UI-facing APIs**.

## Principles Enforced

- **Ledger (PaymentRecord) is the single source of truth**
- **PaymentStatus is always derived** (Paid / PartiallyPaid / Unpaid), never set imperatively
- **Refunds are manual and independent** (stock vs money)
- **Partial payments and partial money refunds** are fully supported
- **PaymentStatus = PartiallyPaid does NOT block further refunds**; refund eligibility is based on net paid > 0
- **Cancellation is terminal** and allowed only after full reversal
- **Cancelled orders are immutable**
- **UI-facing data** (DTOs, notifications, balances) reflect derived state

## Test Coverage

### Sales Order
| Test | Description |
|------|-------------|
| `SalesOrder_FullLifecycle_PaymentRefundCancel_WorksCorrectly` | Create → partial payment → Done → partial stock+money refund → remaining stock → multiple partial money refunds → cancel → immutability |
| `SalesOrder_MultiplePartialPayments_ReachesPaid` | Multiple partial payments → Paid |
| `SalesOrder_MultiplePartialMoneyRefunds_UntilNetPaidZero` | Multiple partial refunds over time until net paid = 0 |
| `SalesOrder_Interleaved_PayRefundPayRefund_WorksCorrectly` | Pay → refund some → pay more → refund remaining |
| `SalesOrder_PartiallyPaid_DoesNotBlockFurtherRefunds` | Regression: PartiallyPaid status does not block further money refunds |
| `SalesOrder_CannotRefundMoney_WhenNetPaidIsZero` | Money refund blocked when net paid = 0 |
| `SalesOrder_CannotRefundStock_WhenStatusNotDone` | Stock refund blocked until order is Done |
| `SalesOrder_CannotCancel_WithNetPaidGreaterThanZero` | Cancel blocked when money remains to refund |
| `SalesOrder_CannotCancel_WithRemainingStock` | Cancel blocked when stock remains to refund |
| `SalesOrder_CannotAddPayment_ToCancelledOrder` | No payments on cancelled orders |
| `SalesOrder_CannotRefund_CancelledOrder` | No refunds on cancelled orders |
| `SalesOrder_CannotUpdateStatus_ToCancelledViaUpdateStatus` | Must use dedicated Cancel operation |

### Purchase Order
| Test | Description |
|------|-------------|
| `PurchaseOrder_FullLifecycle_PaymentRefundCancel_WorksCorrectly` | Create → partial payment → Receive → partial stock+money refund → remaining stock → multiple partial money refunds → cancel |

### Notifications
| Test | Description |
|------|-------------|
| `Notifications_Upcoming_Unpaid_Appears` | Unpaid order with DueDate in future appears |
| `Notifications_Overdue_Unpaid_Appears` | Unpaid order with past DueDate appears |
| `Notifications_Paid_Disappears` | Paid order excluded from unpaid notifications |
| `Notifications_Cancelled_Disappears` | Cancelled order excluded |
| `Notifications_RemainingAmountZero_Disappears` | Fully paid order excluded |

### Payment Methods
| Test | Description |
|------|-------------|
| `Payment_Cash_LedgerCorrect` | Cash payment persists and ledger reflects it |
| `Payment_BankTransfer_RequiresTransferId` | Bank transfer with TransferId persists |
| `Payment_Overpayment_Blocked` | Amount > RemainingAmount throws |
| `Payment_Refund_CreatesNegativeLedgerEntry` | Refund creates correct ledger entry |

### Stock Operations
| Test | Description |
|------|-------------|
| `IssueStock_DoesNotAffectMoney` | Issue does not create FinancialTransactions |
| `AdjustStock_DoesNotAffectMoney` | Adjust does not affect ledger |
| `ProductOnHand_EqualsBatchAggregates` | Product-level OnHand = sum of batch OnHand |

### Cross-View Consistency
| Test | Description |
|------|-------------|
| `SameOrder_ConsistentData_AcrossDetailsCustomerHistoryNotifications` | PaidAmount, RemainingAmount, PaymentStatus consistent across APIs |

### Reporting & Financial Summary
| Test | Description |
|------|-------------|
| `FinancialSummary_TotalSales_COGS_NetProfit_ReflectCompletedOrders` | Completed paid orders reflected in summary |
| `FinancialSummary_Refund_ReducesSalesProfit` | Refunds reduce sales profit in report |
| `InternalExpenses_AppearInReport` | Internal expenses appear in GetInternalExpensesAsync |
| `CancelledOrders_ExcludedFromFinancialSummary` | Cancelled orders without payment excluded |

### Negative & Regression
| Test | Description |
|------|-------------|
| `DoubleCancel_Blocked` | Second cancel throws |
| `DoubleRefund_WhenFullyRefunded_Blocked` | Refund after full refund blocked |
| `Notifications_NeverInclude_PaidOrders` | Paid orders excluded |
| `Notifications_NeverInclude_CancelledOrders` | Cancelled orders excluded |
| `Notifications_NeverInclude_RemainingAmountZero` | Fully paid excluded |

## Running Tests

```powershell
# Run all integration tests
dotnet test tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj

# Run specific test class
dotnet test tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj --filter "FullyQualifiedName~SalesOrderIntegrationTests"

# Run with verbose output
dotnet test tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj --verbosity normal
```

## Architecture

- **Real services** – No domain logic mocked; SalesOrderServices, PurchaseOrderServices, InventoryServices, NotificationService, ReportingServices, etc. are used as in production
- **In-memory database** – `Microsoft.EntityFrameworkCore.InMemory` for isolation and speed
- **TestDataSeeder** – Seeds Category, Product, StockSnapshot, ProductBatch (BATCH-001, OnHand=50), Customer, Supplier
- **ResetAndSeedAsync** – Available for tests that need a clean slate (e.g. full lifecycle tests)

## Invariants Validated

1. **PaymentStatus derivation** – Always computed from PaymentRecords (Paid / PartiallyPaid / Unpaid), never set directly
2. **Stock consistency** – Product OnHand/Reserved derived from ProductBatch aggregates
3. **Refund eligibility** – Stock refund requires Done/Received; money refund requires net paid > 0 (PartiallyPaid does NOT block)
4. **Partial refunds** – Multiple money refund operations allowed until net paid reaches zero
5. **Cancellation** – Allowed only when NetPaid=0 and remaining stock=0; thereafter immutable
6. **Notifications** – Unpaid/overdue orders only; paid and cancelled excluded

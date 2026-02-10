-- Database Cleanup Script
USE [InventoryTest];
GO

-- 1. Financial Transactions
DELETE FROM FinancialTransactions;
PRINT 'Deleted FinancialTransactions';

-- 2. Refund Lines and Transactions
DELETE FROM RefundTransactionLines;
DELETE FROM RefundTransactions;
PRINT 'Deleted Refund Transactions and Lines';

-- 3. Payment Records
DELETE FROM PaymentRecords;
PRINT 'Deleted PaymentRecords';

-- 4. Order Lines
DELETE FROM SalesOrderLines;
DELETE FROM PurchaseOrderLines;
PRINT 'Deleted Order Lines';

-- 5. Inventory Transactions
DELETE FROM InventoryTransactions;
PRINT 'Deleted InventoryTransactions';

-- 6. Orders
DELETE FROM SalesOrders;
DELETE FROM PurchaseOrders;
PRINT 'Deleted Orders';

-- 7. Stock Snapshots
DELETE FROM StockSnapshots;
PRINT 'Deleted StockSnapshots';

-- 8. Reset Product Batch quantities
UPDATE ProductBatches SET OnHand = 0, Reserved = 0;
PRINT 'Reset ProductBatches quantities to 0';

GO

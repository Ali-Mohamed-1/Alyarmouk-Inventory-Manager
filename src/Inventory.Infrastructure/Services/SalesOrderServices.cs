using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services
{
    public sealed class SalesOrderServices : ISalesOrderServices
    {
        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;

        public SalesOrderServices(
            AppDbContext db,
            IAuditLogWriter auditWriter)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        }

        public async Task<long> CreateAsync(CreateSalesOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.CustomerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Customer ID must be positive.");

            if (req.Lines is null || req.Lines.Count == 0)
                throw new ValidationException("Sales order must have at least one line item.");

            // Verify customer exists
            var customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct);

            if (customer is null)
                throw new NotFoundException($"Customer id {req.CustomerId} was not found.");

            // Validate and group line items by product (combine quantities if duplicate products)
            var lineItems = new Dictionary<int, decimal>();
            var productIds = new HashSet<int>();

            foreach (var line in req.Lines)
            {
                if (line.ProductId <= 0)
                    throw new ValidationException("Product ID must be positive for all line items.");

                if (line.Quantity <= 0)
                    throw new ValidationException("Quantity must be greater than zero for all line items.");

                if (lineItems.ContainsKey(line.ProductId))
                {
                    lineItems[line.ProductId] += line.Quantity;
                }
                else
                {
                    lineItems[line.ProductId] = line.Quantity;
                    productIds.Add(line.ProductId);
                }
            }

            // Load all products at once for validation
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);

            if (products.Count != productIds.Count)
            {
                var missingIds = productIds.Except(products.Select(p => p.Id)).ToList();
                throw new NotFoundException($"Product(s) not found: {string.Join(", ", missingIds)}");
            }

            // Verify all products are active
            var inactiveProducts = products.Where(p => !p.IsActive).ToList();
            if (inactiveProducts.Any())
            {
                throw new ValidationException($"Cannot create order with inactive product(s): {string.Join(", ", inactiveProducts.Select(p => p.Name))}");
            }

            // Load stock snapshots for all products
            var productIdsList = productIds.ToList();
            var stockSnapshots = await _db.StockSnapshots
                .Where(s => productIdsList.Contains(s.ProductId))
                .ToListAsync(ct);

            // Verify sufficient available stock for all products (OnHand - Reserved)
            foreach (var kvp in lineItems)
            {
                var productId = kvp.Key;
                var quantity = kvp.Value;

                var snapshot = stockSnapshots.FirstOrDefault(s => s.ProductId == productId);
                var availableStock = snapshot != null ? (snapshot.OnHand - snapshot.Reserved) : 0;

                if (availableStock < quantity)
                {
                    var product = products.First(p => p.Id == productId);
                    var onHand = snapshot?.OnHand ?? 0;
                    var reserved = snapshot?.Reserved ?? 0;
                    throw new ValidationException($"Insufficient stock for product '{product.Name}'. Available: {availableStock} (OnHand: {onHand}, Reserved: {reserved}), Requested: {quantity}");
                }
            }

            // Generate unique order number
            var orderNumber = await GenerateUniqueOrderNumberAsync(ct);

            // Use transaction to ensure all operations are atomic
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Create sales order
                var salesOrder = new SalesOrder
                {
                    OrderNumber = orderNumber,
                    CustomerId = req.CustomerId,
                    CustomerNameSnapshot = customer.Name,
                    Status = SalesOrderStatus.Pending,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = user.UserId,
                    CreatedByUserDisplayName = user.UserDisplayName,
                    Note = req.Note
                };

                _db.SalesOrders.Add(salesOrder);
                await _db.SaveChangesAsync(ct); // Save to get the ID

                // Create order lines and update stock
                foreach (var kvp in lineItems)
                {
                    var productId = kvp.Key;
                    var quantity = kvp.Value;
                    var product = products.First(p => p.Id == productId);

                    // Create order line
                    var orderLine = new SalesOrderLine
                    {
                        SalesOrderId = salesOrder.Id,
                        ProductId = productId,
                        ProductNameSnapshot = product.Name,
                        UnitSnapshot = product.Unit,
                        Quantity = quantity
                    };

                    _db.SalesOrderLines.Add(orderLine);

                    // Reserve stock (increase Reserved, OnHand stays the same)
                    var snapshot = stockSnapshots.FirstOrDefault(s => s.ProductId == productId);
                    if (snapshot is null)
                    {
                        snapshot = new StockSnapshot
                        {
                            ProductId = productId,
                            OnHand = 0,
                            Reserved = 0
                        };
                        _db.StockSnapshots.Add(snapshot);
                    }

                    snapshot.Reserved += quantity; // Reserve stock for pending order
                }

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the order creation
                await _auditWriter.LogCreateAsync<SalesOrder>(
                    salesOrder.Id,
                    user,
                    afterState: new
                    {
                        OrderNumber = salesOrder.OrderNumber,
                        CustomerId = salesOrder.CustomerId,
                        CustomerName = salesOrder.CustomerNameSnapshot,
                        Status = salesOrder.Status,
                        LineCount = lineItems.Count,
                        Note = salesOrder.Note
                    },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return salesOrder.Id;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create sales order due to a database conflict.", ex);
            }
        }

        public async Task<SalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Where(o => o.Id == id)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    Status = o.Status,
                    CreatedUtc = o.CreatedUtc,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot
                    }).ToList()
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<SalesOrderResponseDto>> GetCustomerOrdersAsync(int customerId, int take = 100, CancellationToken ct = default)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    Status = o.Status,
                    CreatedUtc = o.CreatedUtc,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot
                    }).ToList()
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<SalesOrderResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    Status = o.Status,
                    CreatedUtc = o.CreatedUtc,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot
                    }).ToList()
                })
                .ToListAsync(ct);
        }

        public async Task UpdateStatusAsync(long orderId, UpdateSalesOrderStatusRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            // Load the order with lines
            var salesOrder = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            var oldStatus = salesOrder.Status;
            var newStatus = req.Status;

            // If status hasn't changed, nothing to do
            if (oldStatus == newStatus)
                return;

            // Validate status transitions
            ValidateStatusTransition(oldStatus, newStatus);

            // Use transaction to ensure all operations are atomic
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Handle stock changes based on status transition
                await HandleStatusTransitionAsync(salesOrder, oldStatus, newStatus, user, ct);

                // Update order status
                salesOrder.Status = newStatus;

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the status change
                await _auditWriter.LogUpdateAsync<SalesOrder>(
                    salesOrder.Id,
                    user,
                    beforeState: new
                    {
                        Status = oldStatus.ToString()
                    },
                    afterState: new
                    {
                        Status = newStatus.ToString()
                    },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update order status due to a database conflict.", ex);
            }
        }

        private static void ValidateStatusTransition(SalesOrderStatus oldStatus, SalesOrderStatus newStatus)
        {
            // Define valid transitions
            var validTransitions = new Dictionary<SalesOrderStatus, HashSet<SalesOrderStatus>>
            {
                { SalesOrderStatus.Pending, new HashSet<SalesOrderStatus> { SalesOrderStatus.Completed, SalesOrderStatus.Cancelled } },
                { SalesOrderStatus.Completed, new HashSet<SalesOrderStatus> { SalesOrderStatus.Cancelled } },
                { SalesOrderStatus.Cancelled, new HashSet<SalesOrderStatus>() } // Cannot transition from cancelled
            };

            if (!validTransitions.ContainsKey(oldStatus))
                throw new ValidationException($"Invalid current status: {oldStatus}");

            if (!validTransitions[oldStatus].Contains(newStatus))
                throw new ValidationException($"Invalid status transition from {oldStatus} to {newStatus}.");
        }

        private async Task HandleStatusTransitionAsync(
            SalesOrder salesOrder,
            SalesOrderStatus oldStatus,
            SalesOrderStatus newStatus,
            UserContext user,
            CancellationToken ct)
        {
            // Load all stock snapshots for products in this order
            var productIds = salesOrder.Lines.Select(l => l.ProductId).ToList();
            var stockSnapshots = await _db.StockSnapshots
                .Where(s => productIds.Contains(s.ProductId))
                .ToListAsync(ct);

            foreach (var line in salesOrder.Lines)
            {
                var snapshot = stockSnapshots.FirstOrDefault(s => s.ProductId == line.ProductId);
                if (snapshot is null)
                {
                    snapshot = new StockSnapshot
                    {
                        ProductId = line.ProductId,
                        OnHand = 0,
                        Reserved = 0
                    };
                    _db.StockSnapshots.Add(snapshot);
                }

                // Handle transitions
                if (oldStatus == SalesOrderStatus.Pending && newStatus == SalesOrderStatus.Completed)
                {
                    // Pending → Completed: Convert reserved stock to actual issue
                    // Decrease Reserved and OnHand, create Issue transaction
                    snapshot.Reserved -= line.Quantity;
                    snapshot.OnHand -= line.Quantity;

                    var inventoryTransaction = new InventoryTransaction
                    {
                        ProductId = line.ProductId,
                        QuantityDelta = -line.Quantity, // Negative for Issue
                        Type = InventoryTransactionType.Issue,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        UserId = user.UserId,
                        UserDisplayName = user.UserDisplayName,
                        clientId = salesOrder.CustomerId,
                        Note = $"Sales order {salesOrder.OrderNumber} - Status changed to Completed"
                    };

                    _db.InventoryTransactions.Add(inventoryTransaction);
                }
                else if (oldStatus == SalesOrderStatus.Pending && newStatus == SalesOrderStatus.Cancelled)
                {
                    // Pending → Cancelled: Release reservation
                    // Decrease Reserved only, OnHand stays the same
                    snapshot.Reserved -= line.Quantity;
                }
                else if (oldStatus == SalesOrderStatus.Completed && newStatus == SalesOrderStatus.Cancelled)
                {
                    // Completed → Cancelled: Return stock to inventory
                    // Increase OnHand, decrease Reserved (if any was still reserved)
                    snapshot.OnHand += line.Quantity;
                    // Note: Reserved should already be 0 for completed orders, but handle edge cases
                    if (snapshot.Reserved > 0)
                    {
                        snapshot.Reserved = Math.Max(0, snapshot.Reserved - line.Quantity);
                    }

                    var inventoryTransaction = new InventoryTransaction
                    {
                        ProductId = line.ProductId,
                        QuantityDelta = line.Quantity, // Positive for Receive (return)
                        Type = InventoryTransactionType.Receive,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        UserId = user.UserId,
                        UserDisplayName = user.UserDisplayName,
                        clientId = salesOrder.CustomerId,
                        Note = $"Sales order {salesOrder.OrderNumber} - Status changed to Cancelled (stock return)"
                    };

                    _db.InventoryTransactions.Add(inventoryTransaction);
                }
            }
        }

        #region Helper Methods

        private static void ValidateUser(UserContext user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.UserId)) throw new UnauthorizedAccessException("Missing user id.");
            if (string.IsNullOrWhiteSpace(user.UserDisplayName)) throw new UnauthorizedAccessException("Missing user display name.");
        }

        private async Task<string> GenerateUniqueOrderNumberAsync(CancellationToken ct)
        {
            // Generate order number: SO-YYYYMMDD-HHMMSS-Random
            var timestamp = DateTimeOffset.UtcNow;
            var datePart = timestamp.ToString("yyyyMMdd");
            var timePart = timestamp.ToString("HHmmss");
            var randomPart = new Random().Next(100, 999).ToString();

            var baseOrderNumber = $"SO-{datePart}-{timePart}-{randomPart}";
            var orderNumber = baseOrderNumber;

            // Ensure uniqueness (retry if collision)
            int attempts = 0;
            while (await _db.SalesOrders.AnyAsync(o => o.OrderNumber == orderNumber, ct) && attempts < 10)
            {
                randomPart = new Random().Next(100, 999).ToString();
                orderNumber = $"SO-{datePart}-{timePart}-{randomPart}";
                attempts++;
            }

            if (attempts >= 10)
                throw new ConflictException("Could not generate unique order number. Please try again.");

            return orderNumber;
        }

        #endregion
    }
}

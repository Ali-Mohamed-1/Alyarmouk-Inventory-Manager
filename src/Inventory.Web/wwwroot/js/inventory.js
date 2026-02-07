// Demo data - In a real application, this would come from API calls
const demoData = {
    categories: [
        { id: 1, name: "Acids" },
        { id: 2, name: "Bases" },
        { id: 3, name: "Solvents" },
        { id: 4, name: "Oxidizing Agents" },
        { id: 5, name: "Organic Compounds" }
    ],
    products: [
        { productId: 1, sku: "CHEM-001", productName: "Sodium Hydroxide (NaOH)", categoryId: 2, categoryName: "Bases", onHand: 150, preserved: 25, available: 125 },
        { productId: 2, sku: "CHEM-002", productName: "Hydrochloric Acid (HCl)", categoryId: 1, categoryName: "Acids", onHand: 300, preserved: 50, available: 250 },
        { productId: 3, sku: "CHEM-003", productName: "Sulfuric Acid (H2SO4)", categoryId: 1, categoryName: "Acids", onHand: 75, preserved: 10, available: 65 },
        { productId: 4, sku: "CHEM-004", productName: "Ethanol (C2H5OH)", categoryId: 3, categoryName: "Solvents", onHand: 500, preserved: 100, available: 400 },
        { productId: 5, sku: "CHEM-005", productName: "Ammonia Solution (NH3)", categoryId: 2, categoryName: "Bases", onHand: 200, preserved: 30, available: 170 },
        { productId: 6, sku: "CHEM-006", productName: "Potassium Permanganate (KMnO4)", categoryId: 4, categoryName: "Oxidizing Agents", onHand: 120, preserved: 15, available: 105 }
    ],
    customers: [
        { id: 1, name: "Pure Chemical Industries", phone: "+962-6-555-0101", email: "sales@alyarmouk-chem.com", createdUtc: "2025-01-15T10:00:00Z" },
        { id: 2, name: "Jordan Chemical Solutions", phone: "+962-6-555-0102", email: "info@jordanchem.com", createdUtc: "2025-01-16T11:00:00Z" },
        { id: 3, name: "Middle East Chemical Distributors", phone: "+962-6-555-0103", email: "orders@mecd.com", createdUtc: "2025-01-17T09:00:00Z" },
        { id: 4, name: "Amman Industrial Supplies", phone: "+962-6-555-0104", email: "contact@amman-ind.com", createdUtc: "2025-01-18T14:00:00Z" },
        { id: 5, name: "Levant Chemical Company", phone: "+962-6-555-0105", email: "sales@levantchem.com", createdUtc: "2025-01-19T08:00:00Z" }
    ],
    suppliers: [
        { id: 1, name: "Global Chemical Supplies", phone: "+962-6-555-0201", email: "orders@globalchem.com", address: "Amman, Jordan", createdUtc: "2025-01-10T08:00:00Z" },
        { id: 2, name: "International Chemical Importers", phone: "+962-6-555-0202", email: "sales@intlchem.com", address: "Zarqa, Jordan", createdUtc: "2025-01-12T10:00:00Z" },
        { id: 3, name: "Middle East Raw Materials Co.", phone: "+962-6-555-0203", email: "info@merawmaterials.com", address: "Irbid, Jordan", createdUtc: "2025-01-14T09:00:00Z" }
    ],
    orders: [
        {
            id: 1, orderNumber: "SO-2026-001", customerId: 1, customerName: "Pure Chemical Industries",
            createdUtc: "2026-01-20T10:30:00Z", createdBy: "Ahmed Al-Mansour", totalAmount: 1250.00,
            paymentStatus: "Pending", paymentDeadline: "2026-01-31T23:59:00Z", refundAmount: null,
            lines: [
                { productName: "Sodium Hydroxide (NaOH)", quantity: 10, unit: "kg", unitPrice: 75.00, lineTotal: 750.00 },
                { productName: "Hydrochloric Acid (HCl)", quantity: 20, unit: "L", unitPrice: 25.00, lineTotal: 500.00 }
            ]
        },
        {
            id: 2, orderNumber: "SO-2026-002", customerId: 2, customerName: "Jordan Chemical Solutions",
            createdUtc: "2026-01-21T14:15:00Z", createdBy: "Fatima Al-Zahra", totalAmount: 3200.50,
            paymentStatus: "Paid", paymentDeadline: "2026-02-01T23:59:00Z", refundAmount: null,
            lines: [
                { productName: "Sulfuric Acid (H2SO4)", quantity: 15, unit: "L", unitPrice: 150.00, lineTotal: 2250.00 },
                { productName: "Sodium Hydroxide (NaOH)", quantity: 5, unit: "kg", unitPrice: 75.00, lineTotal: 375.00 },
                { productName: "Ethanol (C2H5OH)", quantity: 25, unit: "L", unitPrice: 23.02, lineTotal: 575.50 }
            ]
        },
        {
            id: 3, orderNumber: "SO-2026-003", customerId: 3, customerName: "Middle East Chemical Distributors",
            createdUtc: "2026-01-22T09:00:00Z", createdBy: "Khalid Al-Rashid", totalAmount: 875.25,
            paymentStatus: "Pending", paymentDeadline: "2026-02-05T23:59:00Z", refundAmount: null,
            lines: [
                { productName: "Hydrochloric Acid (HCl)", quantity: 30, unit: "L", unitPrice: 25.00, lineTotal: 750.00 },
                { productName: "Ethanol (C2H5OH)", quantity: 5, unit: "L", unitPrice: 25.05, lineTotal: 125.25 }
            ]
        },
        {
            id: 4, orderNumber: "SO-2026-004", customerId: 4, customerName: "Amman Industrial Supplies",
            createdUtc: "2026-01-23T11:20:00Z", createdBy: "Layla Al-Hashimi", totalAmount: 1950.00,
            paymentStatus: "Refunded", paymentDeadline: "2026-02-06T23:59:00Z", refundAmount: 1950.00,
            lines: [
                { productName: "Ammonia Solution (NH3)", quantity: 20, unit: "L", unitPrice: 65.00, lineTotal: 1300.00 },
                { productName: "Potassium Permanganate (KMnO4)", quantity: 10, unit: "kg", unitPrice: 65.00, lineTotal: 650.00 }
            ]
        }
    ],
    transactions: [
        { id: 1, productId: 1, productName: "Sodium Hydroxide (NaOH)", type: "Issue", quantityDelta: -10, customerId: 1, customerName: "Pure Chemical Industries", userDisplayName: "Ahmed Al-Mansour", timestampUtc: "2026-01-20T10:30:00Z", note: "Sales order SO-2026-001" },
        { id: 2, productId: 2, productName: "Hydrochloric Acid (HCl)", type: "Issue", quantityDelta: -20, customerId: 1, customerName: "Pure Chemical Industries", userDisplayName: "Ahmed Al-Mansour", timestampUtc: "2026-01-20T10:30:00Z", note: "Sales order SO-2026-001" },
        { id: 3, productId: 3, productName: "Sulfuric Acid (H2SO4)", type: "Issue", quantityDelta: -15, customerId: 2, customerName: "Jordan Chemical Solutions", userDisplayName: "Fatima Al-Zahra", timestampUtc: "2026-01-21T14:15:00Z", note: "Sales order SO-2026-002" },
        { id: 4, productId: 1, productName: "Sodium Hydroxide (NaOH)", type: "Receive", quantityDelta: 50, customerId: null, customerName: null, userDisplayName: "Admin", timestampUtc: "2026-01-19T08:00:00Z", note: "Stock replenishment" },
        { id: 5, productId: 2, productName: "Hydrochloric Acid (HCl)", type: "Adjust", quantityDelta: 5, customerId: null, customerName: null, userDisplayName: "Admin", timestampUtc: "2026-01-19T09:00:00Z", note: "Inventory adjustment" },
        { id: 6, productId: 4, productName: "Ethanol (C2H5OH)", type: "Receive", quantityDelta: 100, customerId: null, customerName: null, userDisplayName: "Admin", timestampUtc: "2026-01-18T10:00:00Z", note: "New shipment received" },
        { id: 7, productId: 5, productName: "Ammonia Solution (NH3)", type: "Issue", quantityDelta: -20, customerId: 4, customerName: "Amman Industrial Supplies", userDisplayName: "Layla Al-Hashimi", timestampUtc: "2026-01-23T11:20:00Z", note: "Sales order SO-2026-004" }
    ],
    auditLogs: [
        { id: 1, entityType: "SalesOrder", entityId: "1", action: "Create", userDisplayName: "Ahmed Al-Mansour", timestampUtc: "2026-01-20T10:30:00Z", changesJson: '{"OrderNumber":"SO-2026-001","CustomerId":1,"LineCount":2}' },
        { id: 2, entityType: "SalesOrder", entityId: "2", action: "Create", userDisplayName: "Fatima Al-Zahra", timestampUtc: "2026-01-21T14:15:00Z", changesJson: '{"OrderNumber":"SO-2026-002","CustomerId":2,"LineCount":3}' },
        { id: 3, entityType: "Product", entityId: "1", action: "Update", userDisplayName: "Admin", timestampUtc: "2026-01-19T08:00:00Z", changesJson: '{"Name":"Sodium Hydroxide (NaOH)","ReorderPoint":50}' },
        { id: 4, entityType: "Customer", entityId: "1", action: "Create", userDisplayName: "Admin", timestampUtc: "2025-01-15T10:00:00Z", changesJson: '{"Name":"Pure Chemical Industries","Phone":"+962-6-555-0101"}' },
        { id: 5, entityType: "SalesOrder", entityId: "3", action: "Create", userDisplayName: "Khalid Al-Rashid", timestampUtc: "2026-01-22T09:00:00Z", changesJson: '{"OrderNumber":"SO-2026-003","CustomerId":3,"LineCount":2}' },
        { id: 6, entityType: "Product", entityId: "4", action: "Create", userDisplayName: "Admin", timestampUtc: "2025-12-10T14:00:00Z", changesJson: '{"Name":"Ethanol (C2H5OH)","Sku":"CHEM-004"}' }
    ],
    purchaseOrders: [
        {
            id: 1, orderNumber: "PO-2026-001", supplierId: 1, supplierName: "Global Chemical Supplies",
            createdUtc: "2026-01-15T08:00:00Z", createdBy: "Admin", isTaxInclusive: true,
            subtotal: 86.96, vatAmount: 12.17, manufacturingTaxAmount: 0.87, receiptExpenses: 20.00, totalAmount: 120.00
        },
        {
            id: 2, orderNumber: "PO-2026-002", supplierId: 2, supplierName: "International Chemical Importers",
            createdUtc: "2026-01-18T10:00:00Z", createdBy: "Admin", isTaxInclusive: false,
            subtotal: 200.00, vatAmount: 28.00, manufacturingTaxAmount: 2.00, receiptExpenses: 15.00, totalAmount: 245.00
        },
        {
            id: 3, orderNumber: "PO-2026-003", supplierId: 1, supplierName: "Global Chemical Supplies",
            createdUtc: "2026-01-20T14:00:00Z", createdBy: "Admin", isTaxInclusive: true,
            subtotal: 150.00, vatAmount: 21.00, manufacturingTaxAmount: 1.50, receiptExpenses: 10.00, totalAmount: 182.50
        }
    ],
    expenses: [
        { id: 1, expenseType: "Rent", description: "Monthly office rent", amount: 10.00, expenseDate: "2026-01-01T00:00:00Z", createdBy: "Admin", createdUtc: "2026-01-01T08:00:00Z", note: "January rent payment" }
    ]
};

// Utility Functions
function formatCurrency(amount) {
    return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount);
}

const VAT_RATE = 0.14;
const MANUFACTURING_RATE = 0.01;
const TOTAL_TAX_RATE = VAT_RATE + MANUFACTURING_RATE;

function calculateTaxFromTotal(totalAmount) {
    const subtotal = totalAmount / (1 + TOTAL_TAX_RATE);
    const vatAmount = subtotal * VAT_RATE;
    const manufacturingTaxAmount = subtotal * MANUFACTURING_RATE;
    return { subtotal, vatAmount, manufacturingTaxAmount, totalAmount };
}

function calculateTaxFromSubtotal(subtotal) {
    const vatAmount = subtotal * VAT_RATE;
    const manufacturingTaxAmount = subtotal * MANUFACTURING_RATE;
    const totalAmount = subtotal + vatAmount + manufacturingTaxAmount;
    return { subtotal, vatAmount, manufacturingTaxAmount, totalAmount };
}

function formatDate(dateString) {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
        year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit'
    });
}

function getStockStatus(onHand, preserved, available) {
    if (available <= 0) return '<span class="badge badge-danger">Out of Stock</span>';
    if (available < 50) return '<span class="badge badge-warning">Low Stock</span>';
    return '<span class="badge badge-success">In Stock</span>';
}

function getTransactionTypeBadge(type) {
    const badges = {
        'Receive': '<span class="badge badge-success">Receive</span>',
        'Issue': '<span class="badge badge-danger">Issue</span>',
        'Adjust': '<span class="badge badge-info">Adjust</span>'
    };
    return badges[type] || `<span class="badge">${type}</span>`;
}

function getPaymentStatusBadge(status) {
    const badges = {
        'Pending': '<span class="badge badge-payment badge-pending"><span class="badge-icon">⏰</span> Pending</span>',
        'Paid': '<span class="badge badge-payment badge-paid"><span class="badge-icon">✓</span> Paid</span>',
        'Refunded': '<span class="badge badge-payment badge-refunded"><span class="badge-icon">↩️</span> Refunded</span>'
    };
    return badges[status] || `<span class="badge badge-payment">${status}</span>`;
}

function formatDateTime(dateString) {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        hour12: true
    });
}

function getDeadlineUrgencyClass(deadlineString) {
    if (!deadlineString) return '';
    const deadline = new Date(deadlineString);
    const now = new Date();
    const daysUntilDeadline = Math.ceil((deadline - now) / (1000 * 60 * 60 * 24));

    if (daysUntilDeadline <= 7) return 'deadline-urgent';
    if (daysUntilDeadline <= 30) return 'deadline-warning';
    return '';
}

function calculateTotalOwed(customerId) {
    return demoData.orders
        .filter(o => o.customerId === customerId && o.paymentStatus === 'Pending')
        .reduce((sum, o) => sum + o.totalAmount, 0);
}

function calculatePaymentBreakdown(customerId) {
    const now = new Date();
    const pendingOrders = demoData.orders.filter(o => o.customerId === customerId && o.paymentStatus === 'Pending');

    const breakdown = {
        within7Days: 0,
        within30Days: 0,
        after30Days: 0
    };

    pendingOrders.forEach(order => {
        if (!order.paymentDeadline) return;
        const deadline = new Date(order.paymentDeadline);
        const daysUntil = Math.ceil((deadline - now) / (1000 * 60 * 60 * 24));

        if (daysUntil <= 7) {
            breakdown.within7Days += order.totalAmount;
        } else if (daysUntil <= 30) {
            breakdown.within30Days += order.totalAmount;
        } else {
            breakdown.after30Days += order.totalAmount;
        }
    });

    return breakdown;
}

// Tab Management
function switchTab(tabName) {
    // Hide all tabs
    document.querySelectorAll('.tab-content').forEach(tab => {
        tab.classList.remove('active');
    });
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.remove('active');
    });

    // Show selected tab
    document.getElementById(`tab-${tabName}`).classList.add('active');
    event.target.closest('.tab-btn').classList.add('active');

    // Load tab data
    loadTabData(tabName);
}

function loadTabData(tabName) {
    switch (tabName) {
        case 'dashboard':
            renderProducts();
            updateDashboardStats();
            break;
        case 'orders':
            renderOrders();
            break;
        case 'customers':
            renderCustomers();
            break;
        case 'suppliers':
            renderSuppliers();
            break;
        case 'transactions':
            renderTransactions();
            break;
        case 'financial':
            updateFinancialData();
            break;
            renderTransactions();
            break;
        case 'categories':
            renderCategories();
            break;
        case 'audit':
            renderAuditLogs();
            break;
    }
}

// Dashboard Functions
function renderProducts() {
    const tbody = document.querySelector('#productsTable tbody');
    if (!tbody) return;

    if (demoData.products.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted">No products found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.products.map(p => `
        <tr>
            <td><code>${p.sku}</code></td>
            <td><strong>${p.productName}</strong></td>
            <td><span class="badge badge-info">${p.categoryName}</span></td>
            <td><div class="quantity-badge"><span class="value">${p.onHand.toLocaleString()}</span></div></td>
            <td><div class="quantity-badge"><span class="value text-warning">${p.preserved.toLocaleString()}</span></div></td>
            <td><div class="quantity-badge"><span class="value text-success">${p.available.toLocaleString()}</span></div></td>
            <td>${getStockStatus(p.onHand, p.preserved, p.available)}</td>
        </tr>
    `).join('');
}

function updateDashboardStats() {
    document.getElementById('totalProducts').textContent = demoData.products.length;
    document.getElementById('totalOrders').textContent = demoData.orders.length;

    // Calculate low stock count
    const lowStockCount = demoData.products.filter(p => p.available <= 50).length;
    document.getElementById('lowStockCount').textContent = lowStockCount;

    // Total purchase orders
    const totalPurchaseOrders = demoData.purchaseOrders ? demoData.purchaseOrders.length : 0;
    document.getElementById('totalPurchaseOrders').textContent = totalPurchaseOrders;
}

// Orders Functions
function renderOrders() {
    const tbody = document.querySelector('#ordersTable tbody');
    if (!tbody) return;

    if (demoData.orders.length === 0) {
        tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted">No orders found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.orders.map(o => `
        <tr>
            <td><code>${o.orderNumber}</code></td>
            <td><strong>${o.customerName}</strong></td>
            <td>${formatDate(o.createdUtc)}</td>
            <td>${o.lines.length} item(s)</td>
            <td class="currency"><strong>${formatCurrency(o.totalAmount)}</strong></td>
            <td>${getPaymentStatusBadge(o.paymentStatus || 'Pending')}</td>
            <td>${o.createdBy}</td>
            <td><button class="btn btn-outline btn-sm" onclick="showOrderDetails(${o.id})">View Details</button></td>
        </tr>
    `).join('');
}

function showOrderDetails(orderId) {
    const order = demoData.orders.find(o => o.id === orderId);
    if (!order) return;

    const paymentStatus = order.paymentStatus || 'Pending';
    const paymentDeadline = order.paymentDeadline;
    const refundAmount = order.refundAmount;
    const deadlineClass = paymentDeadline ? getDeadlineUrgencyClass(paymentDeadline) : '';
    const isTaxInclusive = order.isTaxInclusive ?? true;

    let breakdown;
    if (order.subtotal != null && order.vatAmount != null && order.manufacturingTaxAmount != null) {
        breakdown = {
            subtotal: order.subtotal,
            vatAmount: order.vatAmount,
            manufacturingTaxAmount: order.manufacturingTaxAmount,
            totalAmount: order.totalAmount
        };
    } else {
        breakdown = isTaxInclusive
            ? calculateTaxFromTotal(order.totalAmount)
            : calculateTaxFromSubtotal(order.totalAmount);
    }

    const paymentInfoHtml = `
        <div class="payment-info-card">
            <h4 style="margin-bottom: 1rem; font-size: 1.1rem; color: var(--text-primary);">Payment Information</h4>
            <div class="payment-status-row">
                <span class="payment-label">Payment Status:</span>
                ${getPaymentStatusBadge(paymentStatus)}
            </div>
            ${paymentDeadline ? `
                <div class="payment-deadline-row">
                    <span class="payment-label">📅 Payment Deadline:</span>
                    <span class="payment-deadline ${deadlineClass}">${formatDateTime(paymentDeadline)}</span>
                </div>
            ` : ''}
            ${paymentStatus === 'Refunded' && refundAmount ? `
                <div class="payment-refund-row">
                    <span class="payment-label">Refund Amount:</span>
                    <span class="payment-refund-amount">${formatCurrency(refundAmount)}</span>
                </div>
            ` : ''}
        </div>
    `;

    const detailsHtml = `
        <div style="margin-bottom: 1.5rem;">
            <h3>${order.orderNumber}</h3>
            <p class="text-muted">Customer: <strong>${order.customerName}</strong> | Created: ${formatDate(order.createdUtc)} | By: ${order.createdBy}</p>
            <p class="text-muted">Tax Inclusive: <strong>${isTaxInclusive ? 'Yes' : 'No'}</strong></p>
        </div>
        ${paymentInfoHtml}
        <div class="card" style="margin-top: 1.5rem;">
            <div class="card-header">Tax Breakdown</div>
            <div class="card-body">
                <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 1rem;">
                    <div>
                        <div class="text-muted" style="font-size: 0.85rem;">Subtotal (Before Tax)</div>
                        <div class="stat-value currency">${formatCurrency(breakdown.subtotal)}</div>
                    </div>
                    <div>
                        <div class="text-muted" style="font-size: 0.85rem;">VAT (14%)</div>
                        <div class="stat-value currency">${formatCurrency(breakdown.vatAmount)}</div>
                    </div>
                    <div>
                        <div class="text-muted" style="font-size: 0.85rem;">Manufacturing Tax (1%)</div>
                        <div class="stat-value currency">${formatCurrency(breakdown.manufacturingTaxAmount)}</div>
                    </div>
                    <div>
                        <div class="text-muted" style="font-size: 0.85rem;">Total Amount</div>
                        <div class="stat-value currency">${formatCurrency(breakdown.totalAmount)}</div>
                    </div>
                </div>
            </div>
        </div>
        <div class="table-responsive" style="margin-top: 1.5rem;">
            <table class="table">
                <thead>
                    <tr>
                        <th>Product</th>
                        <th class="text-right">Quantity</th>
                        <th class="text-right">Unit Price</th>
                        <th class="text-right">Line Subtotal</th>
                        <th class="text-right">VAT</th>
                        <th class="text-right">Manufacturing Tax</th>
                        <th class="text-right">Line Total</th>
                    </tr>
                </thead>
                <tbody>
                    ${order.lines.map(line => {
        const lineTotal = line.lineTotal ?? (line.quantity * line.unitPrice);
        const lineBreakdown = line.lineSubtotal != null
            ? {
                subtotal: line.lineSubtotal,
                vatAmount: line.lineVatAmount ?? 0,
                manufacturingTaxAmount: line.lineManufacturingTaxAmount ?? 0,
                totalAmount: lineTotal
            }
            : (isTaxInclusive ? calculateTaxFromTotal(lineTotal) : calculateTaxFromSubtotal(lineTotal));

        return `
                            <tr>
                                <td><strong>${line.productName}</strong></td>
                                <td class="text-right">${line.quantity} ${line.unit}</td>
                                <td class="text-right currency">${formatCurrency(line.unitPrice)}</td>
                                <td class="text-right currency">${formatCurrency(lineBreakdown.subtotal)}</td>
                                <td class="text-right currency">${formatCurrency(lineBreakdown.vatAmount)}</td>
                                <td class="text-right currency">${formatCurrency(lineBreakdown.manufacturingTaxAmount)}</td>
                                <td class="text-right currency"><strong>${formatCurrency(lineBreakdown.totalAmount)}</strong></td>
                            </tr>
                        `;
    }).join('')}
                    <tr style="border-top: 2px solid var(--border);">
                        <td colspan="6" class="text-right"><strong>Total Amount:</strong></td>
                        <td class="text-right currency"><strong style="font-size: 1.25rem; color: var(--primary-color);">${formatCurrency(breakdown.totalAmount)}</strong></td>
                    </tr>
                </tbody>
            </table>
        </div>
    `;

    document.getElementById('orderDetails').innerHTML = detailsHtml;
}

// Customer Functions
function renderCustomers() {
    const tbody = document.querySelector('#customersTable tbody');
    if (!tbody) return;

    if (demoData.customers.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No customers found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.customers.map(c => `
        <tr>
            <td>${c.id}</td>
            <td><strong>${c.name}</strong></td>
            <td>${c.phone || '-'}</td>
            <td>${c.email || '-'}</td>
            <td>${formatDate(c.createdUtc)}</td>
            <td><button class="btn btn-outline btn-sm" onclick="showCustomerHistory(${c.id})">View History</button></td>
        </tr>
    `).join('');
}

function showCustomerHistory(customerId) {
    const customer = demoData.customers.find(c => c.id === customerId);
    if (!customer) return;

    const customerOrders = demoData.orders.filter(o => o.customerId === customerId);
    const customerTransactions = demoData.transactions.filter(t => t.customerId === customerId);
    const totalOwed = calculateTotalOwed(customerId);
    const paymentBreakdown = calculatePaymentBreakdown(customerId);

    const totalOwedHtml = `
        <div class="total-owed-card">
            <div class="total-owed-label">Total Amount Owed</div>
            <div class="total-owed-value">${formatCurrency(totalOwed)}</div>
        </div>
    `;

    const breakdownHtml = (paymentBreakdown.within7Days > 0 || paymentBreakdown.within30Days > 0 || paymentBreakdown.after30Days > 0) ? `
        <div class="payment-breakdown-card">
            <h4 style="margin-bottom: 1rem; font-size: 1rem; color: var(--text-primary);">Payment Breakdown by Deadline</h4>
            ${paymentBreakdown.within7Days > 0 ? `
                <div class="breakdown-item">
                    <span class="badge badge-danger">Due within 7 days</span>
                    <span class="breakdown-amount">${formatCurrency(paymentBreakdown.within7Days)}</span>
                </div>
            ` : ''}
            ${paymentBreakdown.within30Days > 0 ? `
                <div class="breakdown-item">
                    <span class="badge badge-warning">Due within 30 days</span>
                    <span class="breakdown-amount">${formatCurrency(paymentBreakdown.within30Days)}</span>
                </div>
            ` : ''}
            ${paymentBreakdown.after30Days > 0 ? `
                <div class="breakdown-item">
                    <span class="badge badge-info">Due after 30 days</span>
                    <span class="breakdown-amount">${formatCurrency(paymentBreakdown.after30Days)}</span>
                </div>
            ` : ''}
        </div>
    ` : '';

    const historyHtml = `
        <div style="margin-bottom: 1.5rem;">
            <h3>${customer.name}</h3>
            <p class="text-muted">Customer ID: ${customer.id} | Phone: ${customer.phone || 'N/A'} | Email: ${customer.email || 'N/A'}</p>
        </div>
        
        ${totalOwed > 0 ? totalOwedHtml : '<div class="total-owed-card"><div class="total-owed-label">Total Amount Owed</div><div class="total-owed-value" style="color: var(--success-color);">$0.00</div></div>'}
        ${totalOwed > 0 ? breakdownHtml : ''}
        
        <h4 class="mb-2" style="margin-top: 2rem;">Order History (${customerOrders.length} orders)</h4>
        ${customerOrders.length > 0 ? `
            <div class="table-responsive mb-3">
                <table class="table">
                    <thead>
                        <tr>
                            <th>Order #</th>
                            <th>Date</th>
                            <th>Items</th>
                            <th class="text-right">Total Amount</th>
                            <th>Payment Status</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${customerOrders.map(o => {
        const paymentStatus = o.paymentStatus || 'Pending';
        let actionButtons = '';

        if (paymentStatus === 'Pending') {
            actionButtons = `
                                    <button class="btn btn-success btn-sm" onclick="markOrderAsPaid(${o.id}, ${customerId})" title="Mark as Paid">
                                        ✓ Mark Paid
                                    </button>
                                `;
        } else if (paymentStatus === 'Paid') {
            actionButtons = `
                                    <button class="btn btn-outline btn-sm" onclick="markOrderAsPending(${o.id}, ${customerId})" title="Mark as Pending">
                                        ↻ Mark Pending
                                    </button>
                                    <button class="btn btn-warning btn-sm" onclick="showRefundModal(${o.id}, ${customerId})" title="Process Refund">
                                        ↩️ Refund
                                    </button>
                                `;
        } else if (paymentStatus === 'Refunded') {
            actionButtons = `
                                    <span class="text-muted" style="font-size: 0.875rem;">Refunded</span>
                                `;
        }

        return `
                                <tr>
                                    <td><code>${o.orderNumber}</code></td>
                                    <td>${formatDate(o.createdUtc)}</td>
                                    <td>${o.lines.length} item(s)</td>
                                    <td class="text-right currency"><strong>${formatCurrency(o.totalAmount)}</strong></td>
                                    <td>${getPaymentStatusBadge(paymentStatus)}</td>
                                    <td>
                                        <div class="payment-actions">
                                            ${actionButtons}
                                        </div>
                                    </td>
                                </tr>
                            `;
    }).join('')}
                    </tbody>
                </table>
            </div>
        ` : '<p class="text-muted">No orders found for this customer.</p>'}
        
        <h4 class="mb-2">Transaction History (${customerTransactions.length} transactions)</h4>
        ${customerTransactions.length > 0 ? `
            <div class="table-responsive">
                <table class="table">
                    <thead>
                        <tr>
                            <th>Product</th>
                            <th>Type</th>
                            <th>Quantity</th>
                            <th>Date</th>
                            <th>Note</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${customerTransactions.map(t => `
                            <tr>
                                <td>${t.productName}</td>
                                <td>${getTransactionTypeBadge(t.type)}</td>
                                <td>${t.quantityDelta > 0 ? '+' : ''}${t.quantityDelta}</td>
                                <td>${formatDate(t.timestampUtc)}</td>
                                <td>${t.note || '-'}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        ` : '<p class="text-muted">No transactions found for this customer.</p>'}
    `;

    document.getElementById('customerHistory').innerHTML = historyHtml;
}

// Payment Status Management Functions
function markOrderAsPaid(orderId, customerId) {
    if (!confirm('Mark this order as paid? This will update the payment status and recalculate the total owed.')) {
        return;
    }

    const order = demoData.orders.find(o => o.id === orderId);
    if (!order) return;

    order.paymentStatus = 'Paid';

    // Refresh customer history to show updated status and recalculated total
    showCustomerHistory(customerId);

    // Also refresh orders table if we're on that tab
    if (document.getElementById('tab-orders').classList.contains('active')) {
        renderOrders();
        // Refresh order details if this order is currently displayed
        const orderDetails = document.getElementById('orderDetails');
        if (orderDetails && orderDetails.innerHTML.includes(order.orderNumber)) {
            showOrderDetails(orderId);
        }
    }
}

function markOrderAsPending(orderId, customerId) {
    if (!confirm('Mark this order as pending? This will update the payment status and recalculate the total owed.')) {
        return;
    }

    const order = demoData.orders.find(o => o.id === orderId);
    if (!order) return;

    order.paymentStatus = 'Pending';

    // Refresh customer history to show updated status and recalculated total
    showCustomerHistory(customerId);

    // Also refresh orders table if we're on that tab
    if (document.getElementById('tab-orders').classList.contains('active')) {
        renderOrders();
        // Refresh order details if this order is currently displayed
        const orderDetails = document.getElementById('orderDetails');
        if (orderDetails && orderDetails.innerHTML.includes(order.orderNumber)) {
            showOrderDetails(orderId);
        }
    }
}

function showRefundModal(orderId, customerId) {
    const order = demoData.orders.find(o => o.id === orderId);
    if (!order) return;

    // Store order and customer IDs for the refund function
    document.getElementById('refundOrderId').value = orderId;
    document.getElementById('refundCustomerId').value = customerId;
    document.getElementById('refundOrderNumber').textContent = order.orderNumber;
    document.getElementById('refundOrderAmount').textContent = formatCurrency(order.totalAmount);
    document.getElementById('refundAmount').value = order.totalAmount.toFixed(2);
    document.getElementById('refundAmount').max = order.totalAmount;

    // Add validation on input
    const refundAmountInput = document.getElementById('refundAmount');
    refundAmountInput.oninput = function () {
        const value = parseFloat(this.value);
        const max = parseFloat(this.max);
        if (value > max) {
            this.setCustomValidity(`Refund amount cannot exceed ${formatCurrency(max)}`);
        } else if (value <= 0) {
            this.setCustomValidity('Refund amount must be greater than 0');
        } else {
            this.setCustomValidity('');
        }
    };

    document.getElementById('refundModal').style.display = 'flex';
}

function closeRefundModal() {
    document.getElementById('refundModal').style.display = 'none';
    document.getElementById('refundForm').reset();
    document.getElementById('refundOrderId').value = '';
    document.getElementById('refundCustomerId').value = '';
}

function submitRefund() {
    const orderId = parseInt(document.getElementById('refundOrderId').value);
    const customerId = parseInt(document.getElementById('refundCustomerId').value);
    const refundAmount = parseFloat(document.getElementById('refundAmount').value);
    const refundNote = document.getElementById('refundNote').value;

    if (!orderId || !refundAmount || refundAmount <= 0) {
        if (window.appMessages) window.appMessages.showError('Please enter a valid refund amount.');
        return;
    }

    const order = demoData.orders.find(o => o.id === orderId);
    if (!order) {
        if (window.appMessages) window.appMessages.showError('Order not found.');
        return;
    }

    if (refundAmount > order.totalAmount) {
        if (window.appMessages) window.appMessages.showError(`Refund amount cannot exceed order total of ${formatCurrency(order.totalAmount)}.`);
        return;
    }

    if (!confirm(`Process refund of ${formatCurrency(refundAmount)} for order ${order.orderNumber}?`)) {
        return;
    }

    // Update order payment status and refund amount
    order.paymentStatus = 'Refunded';
    order.refundAmount = refundAmount;

    // In a real application, this would make an API call to process the refund
    if (window.appMessages) window.appMessages.showSuccess(`Refund of ${formatCurrency(refundAmount)} processed successfully for order ${order.orderNumber}.`);

    closeRefundModal();

    // Refresh customer history to show updated status
    showCustomerHistory(customerId);

    // Also refresh orders table if we're on that tab
    if (document.getElementById('tab-orders').classList.contains('active')) {
        renderOrders();
        // Refresh order details if this order is currently displayed
        const orderDetails = document.getElementById('orderDetails');
        if (orderDetails && orderDetails.innerHTML.includes(order.orderNumber)) {
            showOrderDetails(orderId);
        }
    }
}

// Transactions Functions
function renderTransactions() {
    const tbody = document.querySelector('#transactionsTable tbody');
    if (!tbody) return;

    if (demoData.transactions.length === 0) {
        tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted">No transactions found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.transactions.map(t => `
        <tr>
            <td>${t.id}</td>
            <td><strong>${t.productName}</strong></td>
            <td>${getTransactionTypeBadge(t.type)}</td>
            <td class="${t.quantityDelta > 0 ? 'text-success' : 'text-danger'}">
                ${t.quantityDelta > 0 ? '+' : ''}${t.quantityDelta}
            </td>
            <td>${t.customerName || '-'}</td>
            <td>${t.userDisplayName}</td>
            <td>${formatDate(t.timestampUtc)}</td>
            <td>${t.note || '-'}</td>
        </tr>
    `).join('');
}

// Categories Functions
function renderCategories() {
    const tbody = document.querySelector('#categoriesTable tbody');
    if (!tbody) return;

    if (demoData.categories.length === 0) {
        tbody.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No categories found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.categories.map(c => {
        const productCount = demoData.products.filter(p => p.categoryId === c.id).length;
        return `
            <tr>
                <td>${c.id}</td>
                <td><strong>${c.name}</strong></td>
                <td><span class="badge badge-info">${productCount} product(s)</span></td>
                <td>
                    <button class="btn btn-outline btn-sm" onclick="editCategory(${c.id})">Edit</button>
                </td>
            </tr>
        `;
    }).join('');
}

function showCreateCategoryModal() {
    document.getElementById('createCategoryModal').style.display = 'flex';
}

function closeCreateCategoryModal() {
    document.getElementById('createCategoryModal').style.display = 'none';
    document.getElementById('createCategoryForm').reset();
}

function submitCategory() {
    // In a real app, this would make an API call
    if (window.appMessages) window.appMessages.showError('Category creation would be submitted to the API. This is a demo.');
    closeCreateCategoryModal();
}

function editCategory(categoryId) {
    const category = demoData.categories.find(c => c.id === categoryId);
    if (!category) return;

    // In a real app, this would open an edit modal
    if (window.appMessages) window.appMessages.showError(`Edit category: ${category.name} (ID: ${categoryId})`);
}

// Audit Logs Functions
function renderAuditLogs() {
    const tbody = document.querySelector('#auditTable tbody');
    if (!tbody) return;

    if (demoData.auditLogs.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted">No audit logs found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.auditLogs.map(a => {
        let changes = '';
        try {
            const changesObj = JSON.parse(a.changesJson || '{}');
            changes = Object.entries(changesObj).map(([key, value]) => `${key}: ${value}`).join(', ');
        } catch (e) {
            changes = a.changesJson || '-';
        }

        return `
            <tr>
                <td>${a.id}</td>
                <td><code>${a.entityType}</code></td>
                <td><code>${a.entityId}</code></td>
                <td><span class="badge badge-info">${a.action}</span></td>
                <td>${a.userDisplayName}</td>
                <td>${formatDate(a.timestampUtc)}</td>
                <td><small class="text-muted">${changes || '-'}</small></td>
            </tr>
        `;
    }).join('');
}

// Modal Functions
function showCreateOrderModal() {
    document.getElementById('createOrderModal').style.display = 'flex';
    populateCustomerDropdown();
    populateProductDropdowns();
    updateOrderTotal();
}

function closeCreateOrderModal() {
    document.getElementById('createOrderModal').style.display = 'none';
    document.getElementById('createOrderForm').reset();
    document.getElementById('orderItems').innerHTML = `
        <div class="order-item">
            <select class="form-control order-product" required>
                <option value="">Select product...</option>
            </select>
            <input type="number" class="form-control order-quantity" placeholder="Quantity" min="0.01" step="0.01" required>
            <input type="number" class="form-control order-price" placeholder="Unit Price" min="0" step="0.01" required>
            <button type="button" class="btn btn-outline" onclick="removeOrderItem(this)">Remove</button>
        </div>
    `;
    populateProductDropdowns();
}

function showCreateCustomerModal() {
    document.getElementById('createCustomerModal').style.display = 'flex';
}

function closeCreateCustomerModal() {
    document.getElementById('createCustomerModal').style.display = 'none';
    document.getElementById('createCustomerForm').reset();
}

function populateCustomerDropdown() {
    const select = document.getElementById('orderCustomerId');
    select.innerHTML = '<option value="">Select a customer...</option>' +
        demoData.customers.map(c => `<option value="${c.id}">${c.name}</option>`).join('');
}

function populateSupplierDropdown() {
    const select = document.getElementById('receiveSupplierId');
    if (!select) return;
    select.innerHTML = '<option value="">Select supplier...</option>' +
        (demoData.suppliers || []).map(s => `<option value="${s.id}">${s.name}</option>`).join('');
}

function populateProductDropdowns() {
    document.querySelectorAll('.order-product').forEach(select => {
        if (select.options.length <= 1) {
            select.innerHTML = '<option value="">Select product...</option>' +
                demoData.products.map(p => `<option value="${p.productId}">${p.productName} (${p.sku})</option>`).join('');
        }
    });
}

// Helper function to populate product dropdown (used by stock operations)
function populateProductDropdown(selectId) {
    const select = document.getElementById(selectId);
    if (select) {
        select.innerHTML = '<option value="">Select product...</option>' +
            demoData.products.map(p => `<option value="${p.productId}">${p.productName} (${p.sku}) - Available: ${p.available}</option>`).join('');
    }
}


function addOrderItem() {
    const container = document.getElementById('orderItems');
    const newItem = document.createElement('div');
    newItem.className = 'order-item';
    newItem.innerHTML = `
        <select class="form-control order-product" required>
            <option value="">Select product...</option>
        </select>
        <input type="number" class="form-control order-quantity" placeholder="Quantity" min="0.01" step="0.01" required oninput="updateOrderTotal()">
        <input type="number" class="form-control order-price" placeholder="Unit Price" min="0" step="0.01" required oninput="updateOrderTotal()">
        <button type="button" class="btn btn-outline" onclick="removeOrderItem(this)">Remove</button>
    `;
    container.appendChild(newItem);
    populateProductDropdowns();
}

function removeOrderItem(btn) {
    btn.closest('.order-item').remove();
    updateOrderTotal();
}

function updateOrderTotal() {
    let subtotal = 0;
    let vatAmount = 0;
    let manufacturingTaxAmount = 0;
    let total = 0;
    const isTaxInclusive = document.getElementById('orderTaxInclusive')?.checked ?? true;

    document.querySelectorAll('.order-item').forEach(item => {
        const quantity = parseFloat(item.querySelector('.order-quantity').value) || 0;
        const price = parseFloat(item.querySelector('.order-price').value) || 0;
        const lineAmount = quantity * price;
        if (isTaxInclusive) {
            const breakdown = calculateTaxFromTotal(lineAmount);
            subtotal += breakdown.subtotal;
            vatAmount += breakdown.vatAmount;
            manufacturingTaxAmount += breakdown.manufacturingTaxAmount;
            total += breakdown.totalAmount;
        } else {
            const breakdown = calculateTaxFromSubtotal(lineAmount);
            subtotal += breakdown.subtotal;
            vatAmount += breakdown.vatAmount;
            manufacturingTaxAmount += breakdown.manufacturingTaxAmount;
            total += breakdown.totalAmount;
        }
    });

    document.getElementById('orderSubtotal').textContent = formatCurrency(subtotal);
    document.getElementById('orderVatAmount').textContent = formatCurrency(vatAmount);
    document.getElementById('orderManufacturingTaxAmount').textContent = formatCurrency(manufacturingTaxAmount);
    document.getElementById('orderTotal').textContent = formatCurrency(total);
}

function submitOrder() {
    const form = document.getElementById('createOrderForm');
    if (!form.checkValidity()) {
        form.reportValidity();
        return;
    }

    const customerId = parseInt(document.getElementById('orderCustomerId').value, 10);
    const customer = demoData.customers.find(c => c.id === customerId);
    if (!customer) {
        if (window.appMessages) window.appMessages.showError('Please select a customer.');
        return;
    }

    const isTaxInclusive = document.getElementById('orderTaxInclusive')?.checked ?? true;
    const lines = [];
    let subtotal = 0;
    let vatAmount = 0;
    let manufacturingTaxAmount = 0;
    let totalAmount = 0;

    document.querySelectorAll('.order-item').forEach(item => {
        const productId = parseInt(item.querySelector('.order-product').value, 10);
        const product = demoData.products.find(p => p.productId === productId);
        const quantity = parseFloat(item.querySelector('.order-quantity').value) || 0;
        const unitPrice = parseFloat(item.querySelector('.order-price').value) || 0;
        const lineAmount = quantity * unitPrice;

        if (!product) return;

        const breakdown = isTaxInclusive ? calculateTaxFromTotal(lineAmount) : calculateTaxFromSubtotal(lineAmount);

        subtotal += breakdown.subtotal;
        vatAmount += breakdown.vatAmount;
        manufacturingTaxAmount += breakdown.manufacturingTaxAmount;
        totalAmount += breakdown.totalAmount;

        lines.push({
            productName: product.productName,
            quantity,
            unit: product.unit || 'unit',
            unitPrice,
            lineSubtotal: breakdown.subtotal,
            lineVatAmount: breakdown.vatAmount,
            lineManufacturingTaxAmount: breakdown.manufacturingTaxAmount,
            lineTotal: breakdown.totalAmount
        });
    });

    const newOrder = {
        id: demoData.orders.length + 1,
        orderNumber: `SO-2026-${String(demoData.orders.length + 1).padStart(3, '0')}`,
        customerId,
        customerName: customer.name,
        createdUtc: new Date().toISOString(),
        createdBy: 'Current User',
        totalAmount,
        subtotal,
        vatAmount,
        manufacturingTaxAmount,
        isTaxInclusive,
        paymentStatus: 'Pending',
        paymentDeadline: null,
        refundAmount: null,
        lines
    };

    demoData.orders.push(newOrder);
    renderOrders();
    updateDashboardStats();
    closeCreateOrderModal();
}

function submitCustomer() {
    // In a real app, this would make an API call
    if (window.appMessages) window.appMessages.showError('Customer creation would be submitted to the API. This is a demo.');
    closeCreateCustomerModal();
}

// Supplier Functions
function renderSuppliers() {
    const tbody = document.querySelector('#suppliersTable tbody');
    if (!tbody) return;

    if (!demoData.suppliers || demoData.suppliers.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted">No suppliers found</td></tr>';
        return;
    }

    tbody.innerHTML = demoData.suppliers.map(s => `
        <tr>
            <td>${s.id}</td>
            <td><strong>${s.name}</strong></td>
            <td>${s.phone || '-'}</td>
            <td>${s.email || '-'}</td>
            <td>${s.address || '-'}</td>
            <td>${formatDate(s.createdUtc)}</td>
            <td><button class="btn btn-outline btn-sm" onclick="showSupplierHistory(${s.id})">View History</button></td>
        </tr>
    `).join('');
}

function showSupplierHistory(supplierId) {
    const supplier = demoData.suppliers.find(s => s.id === supplierId);
    if (!supplier) return;

    const supplierOrders = (demoData.purchaseOrders || []).filter(po => po.supplierId === supplierId);
    const totalSpent = supplierOrders.reduce((sum, po) => sum + (po.totalAmount || 0), 0);

    const totalSpentHtml = `
        <div class="total-owed-card">
            <div class="total-owed-label">Total Amount Spent</div>
            <div class="total-owed-value">${formatCurrency(totalSpent)}</div>
        </div>
    `;

    const historyHtml = `
        <div style="margin-bottom: 1.5rem;">
            <h3>${supplier.name}</h3>
            <p class="text-muted">Supplier ID: ${supplier.id} | Phone: ${supplier.phone || 'N/A'} | Email: ${supplier.email || 'N/A'}</p>
            ${supplier.address ? `<p class="text-muted">Address: ${supplier.address}</p>` : ''}
        </div>
        
        ${totalSpentHtml}
        
        <h4 class="mb-2" style="margin-top: 2rem;">Purchase Order History (${supplierOrders.length} orders)</h4>
        ${supplierOrders.length > 0 ? `
            <div class="table-responsive mb-3">
                <table class="table">
                    <thead>
                        <tr>
                            <th>Order #</th>
                            <th>Date</th>
                            <th>Tax Inclusive</th>
                            <th class="text-right">Subtotal</th>
                            <th class="text-right">VAT</th>
                            <th class="text-right">Manufacturing Tax</th>
                            <th class="text-right">Receipt Expenses</th>
                            <th class="text-right">Total Amount</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${supplierOrders.map(po => `
                            <tr>
                                <td><code>${po.orderNumber}</code></td>
                                <td>${formatDate(po.createdUtc)}</td>
                                <td>${po.isTaxInclusive ? '<span class="badge badge-success">Yes</span>' : '<span class="badge badge-info">No</span>'}</td>
                                <td class="text-right currency">${formatCurrency(po.subtotal || 0)}</td>
                                <td class="text-right currency">${formatCurrency(po.vatAmount || 0)}</td>
                                <td class="text-right currency">${formatCurrency(po.manufacturingTaxAmount || 0)}</td>
                                <td class="text-right currency">${formatCurrency(po.receiptExpenses || 0)}</td>
                                <td class="text-right currency"><strong>${formatCurrency(po.totalAmount || 0)}</strong></td>
                                <td><button class="btn btn-outline btn-sm" onclick="showPurchaseOrderDetails('${po.orderNumber}')">View Details</button></td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        ` : `
            <div class="alert alert-info">
                <p class="text-muted text-center">No purchase orders found for this supplier.</p>
            </div>
        `}
    `;

    document.getElementById('supplierHistory').innerHTML = historyHtml;
}

function showPurchaseOrderDetails(orderNumber) {
    const order = (demoData.purchaseOrders || []).find(po => po.orderNumber === orderNumber);
    if (!order) {
        if (window.appMessages) window.appMessages.showError('Purchase order not found');
        return;
    }

    const detailsHtml = `
        <div style="margin-bottom: 1.5rem;">
            <h3>${order.orderNumber}</h3>
            <p class="text-muted">Supplier: <strong>${order.supplierName}</strong> | Created: ${formatDate(order.createdUtc)} | By: ${order.createdBy}</p>
            <p class="text-muted">Tax Inclusive: ${order.isTaxInclusive ? 'Yes' : 'No'}</p>
        </div>
        <div class="table-responsive" style="margin-top: 1.5rem;">
            <table class="table">
                <thead>
                    <tr>
                        <th>Description</th>
                        <th class="text-right">Amount</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td><strong>Subtotal</strong></td>
                        <td class="text-right currency">${formatCurrency(order.subtotal || 0)}</td>
                    </tr>
                    <tr>
                        <td>VAT (14%)</td>
                        <td class="text-right currency">${formatCurrency(order.vatAmount || 0)}</td>
                    </tr>
                    <tr>
                        <td>Manufacturing Tax (1%)</td>
                        <td class="text-right currency">${formatCurrency(order.manufacturingTaxAmount || 0)}</td>
                    </tr>
                    <tr>
                        <td>Receipt Expenses</td>
                        <td class="text-right currency">${formatCurrency(order.receiptExpenses || 0)}</td>
                    </tr>
                    <tr style="border-top: 2px solid var(--border);">
                        <td><strong>Total Amount</strong></td>
                        <td class="text-right currency"><strong style="font-size: 1.25rem; color: var(--primary-color);">${formatCurrency(order.totalAmount || 0)}</strong></td>
                    </tr>
                </tbody>
            </table>
        </div>
    `;

    if (window.appMessages) window.appMessages.showSuccess(`Purchase Order: ${order.orderNumber} | Supplier: ${order.supplierName} | Total: ${formatCurrency(order.totalAmount || 0)}`);
}

function showCreateSupplierModal() {
    document.getElementById('createSupplierModal').style.display = 'flex';
}

function closeCreateSupplierModal() {
    document.getElementById('createSupplierModal').style.display = 'none';
    document.getElementById('createSupplierForm').reset();
}

function submitSupplier() {
    const form = document.getElementById('createSupplierForm');
    if (!form.checkValidity()) {
        form.reportValidity();
        return;
    }

    const supplier = {
        id: (demoData.suppliers?.length || 0) + 1,
        name: document.getElementById('supplierName').value.trim(),
        phone: document.getElementById('supplierPhone').value.trim() || null,
        email: document.getElementById('supplierEmail').value.trim() || null,
        address: document.getElementById('supplierAddress').value.trim() || null,
        createdUtc: new Date().toISOString()
    };

    if (!demoData.suppliers) {
        demoData.suppliers = [];
    }
    demoData.suppliers.push(supplier);

    closeCreateSupplierModal();
    renderSuppliers();
}

// Stock Operations Functions
function showReceiveStockModal() {
    document.getElementById('receiveStockModal').style.display = 'flex';
    populateProductDropdown('receiveProductId');
    populateSupplierDropdown();
    updateReceiveTotals();
}

function closeReceiveStockModal() {
    document.getElementById('receiveStockModal').style.display = 'none';
    document.getElementById('receiveStockForm').reset();
}

function updateReceiveTotals() {
    const quantity = parseFloat(document.getElementById('receiveQuantity').value) || 0;
    const unitCost = parseFloat(document.getElementById('receiveUnitCost').value) || 0;
    const receiptExpenses = parseFloat(document.getElementById('receiveReceiptExpenses').value) || 0;
    const isTaxInclusive = document.getElementById('receiveTaxInclusive')?.checked ?? true;
    const lineAmount = quantity * unitCost;

    const breakdown = isTaxInclusive ? calculateTaxFromTotal(lineAmount) : calculateTaxFromSubtotal(lineAmount);
    const total = breakdown.totalAmount + receiptExpenses;

    document.getElementById('receiveSubtotal').textContent = formatCurrency(breakdown.subtotal);
    document.getElementById('receiveVatAmount').textContent = formatCurrency(breakdown.vatAmount);
    document.getElementById('receiveManufacturingTaxAmount').textContent = formatCurrency(breakdown.manufacturingTaxAmount);
    document.getElementById('receiveTotal').textContent = formatCurrency(total);
}

function submitReceiveStock() {
    const supplierId = document.getElementById('receiveSupplierId').value;
    const productId = document.getElementById('receiveProductId').value;
    const quantity = parseFloat(document.getElementById('receiveQuantity').value);
    const unitCost = parseFloat(document.getElementById('receiveUnitCost').value);
    const isTaxInclusive = document.getElementById('receiveTaxInclusive')?.checked ?? true;
    const receiptExpenses = parseFloat(document.getElementById('receiveReceiptExpenses').value) || 0;
    const note = document.getElementById('receiveNote').value;

    if (!supplierId || !productId || !quantity || quantity <= 0 || unitCost < 0) {
        if (window.appMessages) window.appMessages.showError('Please fill in all required fields with valid values.');
        return;
    }

    // In a real app, this would make an API call to create a Receive transaction
    const product = demoData.products.find(p => p.productId == productId);
    const supplier = (demoData.suppliers || []).find(s => s.id == supplierId);
    if (product && supplier) {
        product.onHand += quantity;
        renderProducts();
        updateDashboardStats();

        const lineAmount = quantity * unitCost;
        const breakdown = isTaxInclusive ? calculateTaxFromTotal(lineAmount) : calculateTaxFromSubtotal(lineAmount);
        const totalAmount = breakdown.totalAmount + receiptExpenses;

        if (!demoData.purchaseOrders) {
            demoData.purchaseOrders = [];
        }

        const newPurchaseOrder = {
            id: demoData.purchaseOrders.length + 1,
            orderNumber: `PO-2026-${String(demoData.purchaseOrders.length + 1).padStart(3, '0')}`,
            supplierId: supplier.id,
            supplierName: supplier.name,
            createdUtc: new Date().toISOString(),
            createdBy: 'Current User',
            isTaxInclusive,
            subtotal: breakdown.subtotal,
            vatAmount: breakdown.vatAmount,
            manufacturingTaxAmount: breakdown.manufacturingTaxAmount,
            receiptExpenses,
            totalAmount
        };
        demoData.purchaseOrders.unshift(newPurchaseOrder);

        // Add to transactions
        const newTransaction = {
            id: demoData.transactions.length + 1,
            productId: parseInt(productId),
            productName: product.productName,
            type: "Receive",
            quantityDelta: quantity,
            customerId: null,
            customerName: null,
            userDisplayName: "Current User",
            timestampUtc: new Date().toISOString(),
            note: note || `Purchase order ${newPurchaseOrder.orderNumber}`
        };
        demoData.transactions.unshift(newTransaction);

        if (document.getElementById('tab-transactions').classList.contains('active')) {
            renderTransactions();
        }
    }

    if (window.appMessages) window.appMessages.showSuccess(`Successfully received ${quantity} units of ${product?.productName || 'product'}`);
    closeReceiveStockModal();
}

function showIssueStockModal() {
    document.getElementById('issueStockModal').style.display = 'flex';
    populateProductDropdown('issueProductId');
    document.getElementById('issueProductId').addEventListener('change', updateIssueAvailableStock);
}

function closeIssueStockModal() {
    document.getElementById('issueStockModal').style.display = 'none';
    document.getElementById('issueStockForm').reset();
    document.getElementById('issueAvailableStock').textContent = '-';
}

function updateIssueAvailableStock() {
    const productId = document.getElementById('issueProductId').value;
    if (productId) {
        const product = demoData.products.find(p => p.productId == productId);
        if (product) {
            document.getElementById('issueAvailableStock').textContent = product.available.toLocaleString();
        }
    } else {
        document.getElementById('issueAvailableStock').textContent = '-';
    }
}

function submitIssueStock() {
    const productId = document.getElementById('issueProductId').value;
    const quantity = parseFloat(document.getElementById('issueQuantity').value);
    const reason = document.getElementById('issueReason').value;
    const note = document.getElementById('issueNote').value;

    if (!productId || !quantity || quantity <= 0 || !reason) {
        if (window.appMessages) window.appMessages.showError('Please fill in all required fields with valid values.');
        return;
    }

    const product = demoData.products.find(p => p.productId == productId);
    if (!product) {
        if (window.appMessages) window.appMessages.showError('Product not found.');
        return;
    }

    if (product.available < quantity) {
        if (window.appMessages) window.appMessages.showError(`Insufficient stock. Available: ${product.available}, Requested: ${quantity}`);
        return;
    }

    // In a real app, this would make an API call to create an Issue transaction
    product.onHand -= quantity;
    renderProducts();
    updateDashboardStats();

    // Add to transactions
    const newTransaction = {
        id: demoData.transactions.length + 1,
        productId: parseInt(productId),
        productName: product.productName,
        type: "Issue",
        quantityDelta: -quantity,
        customerId: null,
        customerName: null,
        userDisplayName: "Current User",
        timestampUtc: new Date().toISOString(),
        note: `${reason}${note ? ': ' + note : ''}`
    };
    demoData.transactions.unshift(newTransaction);

    if (document.getElementById('tab-transactions').classList.contains('active')) {
        renderTransactions();
    }

    if (window.appMessages) window.appMessages.showSuccess(`Successfully issued ${quantity} units of ${product.productName} (Reason: ${reason})`);
    closeIssueStockModal();
}

function showAdjustStockModal() {
    document.getElementById('adjustStockModal').style.display = 'flex';
    populateProductDropdown('adjustProductId');
    document.getElementById('adjustProductId').addEventListener('change', updateAdjustCurrentStock);
    document.getElementById('adjustNewQuantity').addEventListener('input', updateAdjustDifference);
}

function closeAdjustStockModal() {
    document.getElementById('adjustStockModal').style.display = 'none';
    document.getElementById('adjustStockForm').reset();
    document.getElementById('adjustCurrentOnHand').value = '';
    document.getElementById('adjustDifference').textContent = '0';
}

function updateAdjustCurrentStock() {
    const productId = document.getElementById('adjustProductId').value;
    if (productId) {
        const product = demoData.products.find(p => p.productId == productId);
        if (product) {
            document.getElementById('adjustCurrentOnHand').value = product.onHand.toLocaleString();
            updateAdjustDifference();
        }
    } else {
        document.getElementById('adjustCurrentOnHand').value = '';
        document.getElementById('adjustDifference').textContent = '0';
    }
}

function updateAdjustDifference() {
    const productId = document.getElementById('adjustProductId').value;
    const newQuantity = parseFloat(document.getElementById('adjustNewQuantity').value) || 0;

    if (productId) {
        const product = demoData.products.find(p => p.productId == productId);
        if (product) {
            const difference = newQuantity - product.onHand;
            const diffElement = document.getElementById('adjustDifference');
            diffElement.textContent = difference > 0 ? `+${difference.toLocaleString()}` : difference.toLocaleString();
            diffElement.className = difference > 0 ? 'text-success' : difference < 0 ? 'text-danger' : '';
        }
    }
}

function submitAdjustStock() {
    const productId = document.getElementById('adjustProductId').value;
    const newQuantity = parseFloat(document.getElementById('adjustNewQuantity').value);
    const reason = document.getElementById('adjustReason').value;
    const note = document.getElementById('adjustNote').value;

    if (!productId || newQuantity < 0 || !reason) {
        if (window.appMessages) window.appMessages.showError('Please fill in all required fields with valid values.');
        return;
    }

    const product = demoData.products.find(p => p.productId == productId);
    if (!product) {
        if (window.appMessages) window.appMessages.showError('Product not found.');
        return;
    }

    const oldQuantity = product.onHand;
    const difference = newQuantity - oldQuantity;

    // In a real app, this would make an API call to create an Adjust transaction
    product.onHand = newQuantity;
    renderProducts();
    updateDashboardStats();

    // Add to transactions
    const newTransaction = {
        id: demoData.transactions.length + 1,
        productId: parseInt(productId),
        productName: product.productName,
        type: "Adjust",
        quantityDelta: difference,
        customerId: null,
        customerName: null,
        userDisplayName: "Current User",
        timestampUtc: new Date().toISOString(),
        note: `${reason}${note ? ': ' + note : ''} (Adjusted from ${oldQuantity} to ${newQuantity})`
    };
    demoData.transactions.unshift(newTransaction);

    if (document.getElementById('tab-transactions').classList.contains('active')) {
        renderTransactions();
    }

    if (window.appMessages) window.appMessages.showSuccess(`Successfully adjusted stock for ${product.productName} from ${oldQuantity} to ${newQuantity} (Reason: ${reason})`);
    closeAdjustStockModal();
}


// Initialize on page load
document.addEventListener('DOMContentLoaded', function () {
    loadTabData('dashboard');

    // Close modals when clicking outside
    window.onclick = function (event) {
        const orderModal = document.getElementById('createOrderModal');
        const customerModal = document.getElementById('createCustomerModal');
        const supplierModal = document.getElementById('createSupplierModal');
        const categoryModal = document.getElementById('createCategoryModal');
        const receiveModal = document.getElementById('receiveStockModal');
        const issueModal = document.getElementById('issueStockModal');
        const adjustModal = document.getElementById('adjustStockModal');

        if (event.target === orderModal) {
            closeCreateOrderModal();
        }
        if (event.target === customerModal) {
            closeCreateCustomerModal();
        }
        if (event.target === supplierModal) {
            closeCreateSupplierModal();
        }
        if (event.target === categoryModal) {
            closeCreateCategoryModal();
        }
        if (event.target === receiveModal) {
            closeReceiveStockModal();
        }
        if (event.target === issueModal) {
            closeIssueStockModal();
        }
        if (event.target === adjustModal) {
            closeAdjustStockModal();
        }

        const refundModal = document.getElementById('refundModal');
        if (event.target === refundModal) {
            closeRefundModal();
        }

        const addExpenseModal = document.getElementById('addExpenseModal');
        if (event.target === addExpenseModal) {
            closeAddExpenseModal();
        }
    };

    // Initialize financial tab on page load
    initializeFinancialTab();
});

// Financial Tracking Functions
function initializeFinancialTab() {
    // Set default period to current month
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0);

    document.getElementById('periodFromDate').value = firstDay.toISOString().split('T')[0];
    document.getElementById('periodToDate').value = lastDay.toISOString().split('T')[0];

    updateFinancialData();
}

function updatePeriodFromPreset() {
    const preset = document.getElementById('periodPreset').value;
    const now = new Date();
    let fromDate, toDate;

    switch (preset) {
        case 'today':
            fromDate = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            toDate = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 23, 59, 59);
            break;
        case 'week':
            const dayOfWeek = now.getDay();
            fromDate = new Date(now);
            fromDate.setDate(now.getDate() - dayOfWeek);
            toDate = new Date(fromDate);
            toDate.setDate(fromDate.getDate() + 6);
            break;
        case 'month':
            fromDate = new Date(now.getFullYear(), now.getMonth(), 1);
            toDate = new Date(now.getFullYear(), now.getMonth() + 1, 0);
            break;
        case 'quarter':
            const quarter = Math.floor(now.getMonth() / 3);
            fromDate = new Date(now.getFullYear(), quarter * 3, 1);
            toDate = new Date(now.getFullYear(), (quarter + 1) * 3, 0);
            break;
        case 'year':
            fromDate = new Date(now.getFullYear(), 0, 1);
            toDate = new Date(now.getFullYear(), 11, 31);
            break;
        case 'custom':
            // Don't change dates, let user select
            return;
    }

    document.getElementById('periodFromDate').value = fromDate.toISOString().split('T')[0];
    document.getElementById('periodToDate').value = toDate.toISOString().split('T')[0];
    updateFinancialData();
}

function updateFinancialData() {
    const fromDate = new Date(document.getElementById('periodFromDate').value);
    const toDate = new Date(document.getElementById('periodToDate').value);
    toDate.setHours(23, 59, 59, 999);

    // Calculate profitability
    calculateProfitability(fromDate, toDate);

    // Calculate tax liabilities
    calculateTaxLiabilities(fromDate, toDate);

    // Load expenses
    loadExpenses(fromDate, toDate);
}

function calculateProfitability(fromDate, toDate) {
    // Revenue from sales orders
    const revenue = demoData.orders
        .filter(o => {
            const orderDate = new Date(o.createdUtc);
            return orderDate >= fromDate && orderDate <= toDate;
        })
        .reduce((sum, o) => sum + o.totalAmount, 0);

    // Cost of goods sold from purchase orders
    const costOfGoodsSold = (demoData.purchaseOrders || [])
        .filter(po => {
            const orderDate = new Date(po.createdUtc);
            return orderDate >= fromDate && orderDate <= toDate;
        })
        .reduce((sum, po) => sum + po.totalAmount, 0);

    // Internal expenses
    const internalExpenses = (demoData.expenses || [])
        .filter(e => {
            const expenseDate = new Date(e.expenseDate);
            return expenseDate >= fromDate && expenseDate <= toDate;
        })
        .reduce((sum, e) => sum + e.amount, 0);

    const grossProfit = revenue - costOfGoodsSold;
    const netProfit = grossProfit - internalExpenses;
    const profitMargin = revenue > 0 ? (netProfit / revenue) * 100 : 0;

    // Update UI
    document.getElementById('revenue').textContent = formatCurrency(revenue);
    document.getElementById('costOfGoodsSold').textContent = formatCurrency(costOfGoodsSold);
    document.getElementById('grossProfit').textContent = formatCurrency(grossProfit);
    document.getElementById('grossProfitDetail').textContent = formatCurrency(grossProfit);
    document.getElementById('internalExpensesTotal').textContent = formatCurrency(internalExpenses);
    document.getElementById('netProfit').textContent = formatCurrency(netProfit);
    document.getElementById('netProfitDetail').textContent = formatCurrency(netProfit);
    document.getElementById('profitMargin').textContent = profitMargin.toFixed(2) + '%';
    document.getElementById('profitMargin').style.color = profitMargin >= 0 ? 'var(--success)' : 'var(--danger)';
}

function calculateTaxLiabilities(fromDate, toDate) {
    // VAT Collected from sales orders
    const vatCollected = demoData.orders
        .filter(o => {
            const orderDate = new Date(o.createdUtc);
            return orderDate >= fromDate && orderDate <= toDate;
        })
        .reduce((sum, o) => {
            // Calculate VAT from total (14% of base price)
            const basePrice = o.totalAmount / 1.15;
            return sum + (basePrice * 0.14);
        }, 0);

    // VAT Paid on purchase orders (only tax-inclusive)
    const vatPaid = (demoData.purchaseOrders || [])
        .filter(po => {
            const orderDate = new Date(po.createdUtc);
            return orderDate >= fromDate && orderDate <= toDate && po.isTaxInclusive;
        })
        .reduce((sum, po) => sum + (po.vatAmount || 0), 0);

    // Manufacturing Tax Collected
    const manufacturingTaxCollected = demoData.orders
        .filter(o => {
            const orderDate = new Date(o.createdUtc);
            return orderDate >= fromDate && orderDate <= toDate;
        })
        .reduce((sum, o) => {
            const basePrice = o.totalAmount / 1.15;
            return sum + (basePrice * 0.01);
        }, 0);

    // Manufacturing Tax Paid
    const manufacturingTaxPaid = (demoData.purchaseOrders || [])
        .filter(po => {
            const orderDate = new Date(po.createdUtc);
            return orderDate >= fromDate && orderDate <= toDate && po.isTaxInclusive;
        })
        .reduce((sum, po) => sum + (po.manufacturingTaxAmount || 0), 0);

    const vatPayable = vatCollected - vatPaid;
    const manufacturingTaxPayable = manufacturingTaxCollected - manufacturingTaxPaid;
    const totalTaxLiability = vatPayable + manufacturingTaxPayable;

    // Update UI
    document.getElementById('vatPayable').textContent = formatCurrency(vatPayable);
    document.getElementById('vatPayable').style.color = vatPayable >= 0 ? 'var(--danger)' : 'var(--success)';
    document.getElementById('manufacturingTaxPayable').textContent = formatCurrency(manufacturingTaxPayable);
    document.getElementById('manufacturingTaxPayable').style.color = manufacturingTaxPayable >= 0 ? 'var(--danger)' : 'var(--success)';
    document.getElementById('totalTaxLiability').textContent = formatCurrency(totalTaxLiability);
    document.getElementById('totalTaxLiability').style.color = totalTaxLiability >= 0 ? 'var(--danger)' : 'var(--success)';
}

function loadExpenses(fromDate, toDate) {
    const expenses = (demoData.expenses || [])
        .filter(e => {
            const expenseDate = new Date(e.expenseDate);
            return expenseDate >= fromDate && expenseDate <= toDate;
        })
        .sort((a, b) => new Date(b.expenseDate) - new Date(a.expenseDate));

    const tbody = document.querySelector('#expensesTable tbody');
    if (!tbody) return;

    if (expenses.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No expenses found for this period</td></tr>';
        return;
    }

    tbody.innerHTML = expenses.map(e => `
        <tr>
            <td>${formatDate(e.expenseDate)}</td>
            <td><span class="badge badge-info">${e.expenseType}</span></td>
            <td>${e.description}</td>
            <td class="currency">${formatCurrency(e.amount)}</td>
            <td>${e.createdBy}</td>
            <td>
                <button class="btn btn-outline btn-sm" onclick="deleteExpense(${e.id})">Delete</button>
            </td>
        </tr>
    `).join('');
}

function showAddExpenseModal() {
    document.getElementById('addExpenseModal').style.display = 'block';
    // Set default date to today
    document.getElementById('expenseDate').value = new Date().toISOString().split('T')[0];
    document.getElementById('addExpenseForm').reset();
    document.getElementById('expenseDate').value = new Date().toISOString().split('T')[0];
}

function closeAddExpenseModal() {
    document.getElementById('addExpenseModal').style.display = 'none';
}

function submitExpense() {
    const form = document.getElementById('addExpenseForm');
    if (!form.checkValidity()) {
        form.reportValidity();
        return;
    }

    const expense = {
        id: (demoData.expenses?.length || 0) + 1,
        expenseType: document.getElementById('expenseType').value,
        description: document.getElementById('expenseDescription').value,
        amount: parseFloat(document.getElementById('expenseAmount').value),
        expenseDate: document.getElementById('expenseDate').value + 'T00:00:00Z',
        createdBy: 'Current User', // In real app, get from auth
        createdUtc: new Date().toISOString(),
        note: document.getElementById('expenseNote').value || null
    };

    if (!demoData.expenses) {
        demoData.expenses = [];
    }
    demoData.expenses.push(expense);

    closeAddExpenseModal();
    updateFinancialData();
}

function deleteExpense(id) {
    if (!confirm('Are you sure you want to delete this expense?')) return;

    if (demoData.expenses) {
        demoData.expenses = demoData.expenses.filter(e => e.id !== id);
        updateFinancialData();
    }
}

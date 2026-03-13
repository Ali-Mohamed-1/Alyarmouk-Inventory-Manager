/**
 * Dashboard Operations JS
 * Handles dynamic multi-product "Receive Stock" workflow and "Sales Order" shortcut.
 */

(function () {
    const state = {
        suppliers: [],
        products: [],
        selectedSupplierId: null,
        lineCount: 0
    };

    const vatRate = 0.14;
    const manTaxRate = 0.01;

    // DOM Elements
    const receiveModalEl = document.getElementById('receiveStockModal');
    const form = document.getElementById('receiveStockForm');
    const btnSubmit = document.getElementById('btn-receive-submit');
    const btnAddLine = document.getElementById('btn-add-line');
    const supplierSelect = document.getElementById('receiveSupplierId');
    const linesTableBody = document.querySelector('#receiveLinesTable tbody');

    // Initialization
    document.addEventListener('DOMContentLoaded', () => {
        initEventListeners();
        fetchSuppliers();
    });

    function initEventListeners() {
        // Sales Order Shortcut
        const btnSalesOrder = document.getElementById('btn-sales-order-shortcut');
        if (btnSalesOrder) {
            btnSalesOrder.addEventListener('click', () => {
                window.location.href = '/SalesOrders/Create';
            });
        }

        // Add Line Item
        btnAddLine.addEventListener('click', () => {
            if (!state.selectedSupplierId) {
                if (window.appMessages) window.appMessages.showError('Please select a supplier first.');
                return;
            }
            addNewLine();
        });

        // Supplier Selection
        supplierSelect.addEventListener('change', (e) => {
            state.selectedSupplierId = e.target.value;
            linesTableBody.innerHTML = ''; // Clear lines when supplier changes
            state.lineCount = 0;

            if (state.selectedSupplierId) {
                fetchSupplierProducts(state.selectedSupplierId);
                addNewLine(); // Add first line automatically
            }
            calculateSummary();
        });

        // Global calculation triggers
        ['receiveApplyVat', 'receiveApplyManTax', 'receiveExpenses'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.addEventListener('input', calculateSummary);
        });

        // Submit
        btnSubmit.addEventListener('click', submitWorkflow);

        // Reset modal on hide
        receiveModalEl.addEventListener('hidden.bs.modal', () => {
            form.reset();
            linesTableBody.innerHTML = '';
            state.selectedSupplierId = null;
            state.lineCount = 0;
            calculateSummary();
        });
    }

    async function fetchSuppliers() {
        try {
            const resp = await fetch('/Lookups/Suppliers');
            if (!resp.ok) throw new Error();
            state.suppliers = await resp.json();

            supplierSelect.innerHTML = '<option value="">-- SELECT SUPPLIER --</option>' +
                state.suppliers.map(s => `<option value="${s.id}">${s.name}</option>`).join('');
        } catch (err) {
            console.error('Failed to load suppliers', err);
            supplierSelect.innerHTML = '<option value="">ERROR LOADING SUPPLIERS</option>';
        }
    }

    async function fetchSupplierProducts(supplierId) {
        try {
            const resp = await fetch(`/Lookups/SupplierProducts?supplierId=${supplierId}`);
            if (!resp.ok) throw new Error();
            state.products = await resp.json();

            updateAllProductDropdowns();
        } catch (err) {
            console.error('Failed to load supplier products', err);
        }
    }

    function addNewLine() {
        state.lineCount++;
        const rowId = `line-row-${state.lineCount}`;
        const row = document.createElement('tr');
        row.id = rowId;

        // Swapped Column Order: Product -> Qty -> Batch -> Cost -> Action
        row.innerHTML = `
            <td>
                <select class="form-select border-black product-select" required>
                    <option value="">-- SELECT PRODUCT --</option>
                    ${state.products.map(p => `
                        <option value="${p.productId}" data-cost="${p.lastUnitPrice || ''}" data-batch="${p.lastBatchNumber || ''}">
                            ${p.name} (${p.sku})
                        </option>`).join('')}
                </select>
            </td>
            <td>
                <input type="number" class="form-control border-black qty-input" step="0.01" min="0.01" required value="1" />
            </td>
            <td>
                <input type="text" class="form-control border-black batch-input" placeholder="New or Select..." list="list-${rowId}" />
                <datalist id="list-${rowId}"></datalist>
            </td>
            <td>
                <div class="input-group input-group-sm">
                    <span class="input-group-text bg-light text-black border-black">$</span>
                    <input type="number" class="form-control border-black cost-input" step="0.01" min="0" required />
                </div>
            </td>
            <td class="text-center">
                <button type="button" class="btn btn-outline-danger btn-sm border-2 remove-line" data-row="${rowId}">
                    <i class="bi bi-trash"></i>
                </button>
            </td>
        `;

        linesTableBody.appendChild(row);

        const pSelect = row.querySelector('.product-select');
        const batchInput = row.querySelector('.batch-input');
        const batchList = row.querySelector(`#list-${rowId}`);
        const costInput = row.querySelector('.cost-input');

        // Product selection handler
        pSelect.addEventListener('change', async () => {
            const opt = pSelect.selectedOptions[0];
            const pid = pSelect.value;

            // Reset fields
            batchInput.value = '';
            costInput.value = '';
            batchList.innerHTML = '';

            if (pid) {
                // If product provides a default last used cost, use it initially
                // REMOVED at User Request: Don't auto-load last cost. Only load on logical batch selection.
                // if (opt && opt.dataset.cost) {
                //    costInput.value = opt.dataset.cost;
                // }

                // Load Batches for Datalist
                try {
                    const batches = await fetchProductBatches(pid);
                    batchList.innerHTML = batches.map(b =>
                        `<option value="${b.batchNumber}" data-cost="${b.unitCost || ''}">Qty: ${b.onHand}</option>`
                    ).join('');
                } catch (e) {
                    console.error("Failed to load batches", e);
                }
            }
            calculateSummary();
        });

        // Batch selection auto-fill Cost logic
        batchInput.addEventListener('input', () => {
            const val = batchInput.value;
            const options = Array.from(batchList.options);
            const matchedOption = options.find(o => o.value === val);

            if (matchedOption && matchedOption.dataset.cost) {
                costInput.value = matchedOption.dataset.cost;
            }
            calculateSummary();
        });

        row.querySelector('.qty-input').addEventListener('input', calculateSummary);
        costInput.addEventListener('input', calculateSummary);
        row.querySelector('.remove-line').addEventListener('click', (e) => {
            row.remove();
            calculateSummary();
        });

        calculateSummary();
    }

    async function fetchProductBatches(productId) {
        const r = await fetch(`/api/products/${productId}/batches`);
        if (!r.ok) throw new Error();
        return await r.json();
    }

    function updateAllProductDropdowns() {
        const selects = document.querySelectorAll('.product-select');
        const options = '<option value="">-- SELECT PRODUCT --</option>' +
            state.products.map(p => `<option value="${p.productId}" data-cost="${p.lastUnitPrice || ''}" data-batch="${p.lastBatchNumber || ''}">${p.name} (${p.sku})</option>`).join('');

        selects.forEach(s => {
            const currentVal = s.value;
            s.innerHTML = options;
            s.value = currentVal;
        });
    }

    function calculateSummary() {
        let subtotal = 0; // The total of all lines (unit price * qty)
        const applyVat = document.getElementById('receiveApplyVat').checked;
        const applyManTax = document.getElementById('receiveApplyManTax').checked;
        const expenses = parseFloat(document.getElementById('receiveExpenses').value) || 0;

        const rows = linesTableBody.querySelectorAll('tr');
        rows.forEach(row => {
            const qty = parseFloat(row.querySelector('.qty-input').value) || 0;
            const cost = parseFloat(row.querySelector('.cost-input').value) || 0;
            subtotal += (qty * cost);
        });

        // Model: Unit Price is Base Price (Tax Exclusive)
        const vat = applyVat ? (subtotal * vatRate) : 0;
        const manTax = applyManTax ? (subtotal * manTaxRate) : 0;

        const totalDue = subtotal + vat - manTax + expenses;

        document.getElementById('summary-subtotal').textContent = formatCurrency(subtotal);
        document.getElementById('summary-vat').textContent = formatCurrency(vat);
        document.getElementById('summary-mantax').textContent = '-' + formatCurrency(manTax);
        document.getElementById('summary-expenses').textContent = formatCurrency(expenses);
        document.getElementById('summary-total').textContent = formatCurrency(totalDue);
    }

    function formatCurrency(num) {
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(num);
    }

    async function submitWorkflow() {
        if (!validateForm()) return;

        const rows = linesTableBody.querySelectorAll('tr');
        if (rows.length === 0) {
            if (window.appMessages) window.appMessages.showError('Please add at least one product line.');
            return;
        }

        btnSubmit.disabled = true;
        btnSubmit.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>PROCESSING...';

        const payload = {
            supplierId: parseInt(supplierSelect.value),
            note: document.getElementById('receiveNote').value,
            dueDate: document.getElementById('receiveDueDate').value || null,
            connectToReceiveStock: true,
            isTaxInclusive: false, // Updated to Base Price model
            applyVat: document.getElementById('receiveApplyVat').checked,
            applyManufacturingTax: document.getElementById('receiveApplyManTax').checked,
            receiptExpenses: parseFloat(document.getElementById('receiveExpenses').value) || 0,
            lines: Array.from(rows).map(row => ({
                productId: parseInt(row.querySelector('.product-select').value),
                quantity: parseFloat(row.querySelector('.qty-input').value),
                unitPrice: parseFloat(row.querySelector('.cost-input').value),
                batchNumber: row.querySelector('.batch-input').value
            }))
        };

        try {
            const resp = await fetch('/api/purchase-orders', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!resp.ok) {
                const errText = await resp.text();
                const msg = window.appMessages ? await window.appMessages.getApiErrorMessage(resp, errText) : errText;
                throw new Error(msg);
            }

            // Success - Close Modal Robustly
            try {
                const modalEl = document.getElementById('receiveStockModal');
                if (modalEl && typeof bootstrap !== 'undefined') {
                    const modal = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
                    modal.hide();
                }
            } catch (e) {
                console.warn('Modal hide failed', e);
            }

            // Forced cleanup and refresh
            setTimeout(() => {
                document.querySelectorAll('.modal-backdrop').forEach(el => el.remove());
                document.body.classList.remove('modal-open');
                document.body.style.overflow = '';
                document.body.style.paddingRight = '';

                window.location.reload();
            }, 150);
        } catch (err) {
            console.error('Submission failed', err);
            if (window.appMessages) window.appMessages.showError(err.message || 'Failed to complete receipt. Please check your data.');
        } finally {
            btnSubmit.disabled = false;
            btnSubmit.innerHTML = '<i class="bi bi-check2-circle me-1"></i> COMPLETE RECEIPT';
        }
    }

    function validateForm() {
        const inputs = form.querySelectorAll('input[required], select[required]');
        let valid = true;

        inputs.forEach(input => {
            const val = input.value;
            if (!val || (input.type === 'number' && parseFloat(val) < 0)) {
                input.classList.add('is-invalid');
                valid = false;
            } else if (input.classList.contains('qty-input') && parseFloat(val) <= 0) {
                input.classList.add('is-invalid');
                valid = false;
            } else {
                input.classList.remove('is-invalid');
            }
        });

        return valid;
    }
})();

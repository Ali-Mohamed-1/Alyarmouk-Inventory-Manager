// Shared message UI: no browser alert() popups. Uses toast (same component everywhere).
// API errors: use { userMessage } from response.

window.appMessages = {
    async getApiErrorMessage(response, bodyText) {
        const fallback = 'Something went wrong. Please try again.';
        if (bodyText) {
            try {
                const j = JSON.parse(bodyText);
                if (j && typeof j.userMessage === 'string') return j.userMessage;
                if (j && typeof j.detail === 'string') return j.detail;
                if (j && typeof j.title === 'string') return j.title;
            } catch (_) { /* ignore */ }
        }
        return fallback;
    },

    _showToast(message, type) {
        const text = (typeof message === 'string' && message.trim()) ? message.trim() : (type === 'error' ? 'Something went wrong. Please try again.' : 'Action completed successfully.');
        const container = document.getElementById('appToastContainer');
        if (!container) return;
        const isSuccess = type === 'success';
        const bg = isSuccess ? 'bg-success text-white' : 'bg-danger text-white';
        const icon = isSuccess ? 'bi-check-circle-fill' : 'bi-exclamation-triangle-fill';
        const toastEl = document.createElement('div');
        toastEl.className = 'toast align-items-center border-0 ' + bg;
        toastEl.setAttribute('role', 'alert');
        toastEl.innerHTML = '<div class="d-flex"><div class="toast-body d-flex align-items-center"><i class="bi ' + icon + ' me-2 flex-shrink-0"></i><span>' + (text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')) + '</span></div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div>';
        container.appendChild(toastEl);
        const toast = new bootstrap.Toast(toastEl, { delay: 5000, autohide: true });
        toast.show();
        toastEl.addEventListener('hidden.bs.toast', function () { toastEl.remove(); });
    },

    showError(message) {
        this._showToast(message, 'error');
    },

    showSuccess(message) {
        this._showToast(message, 'success');
    }
};

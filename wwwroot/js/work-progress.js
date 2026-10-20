/* FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-001 */
(() => {
    let pending = false, dragged = null;
    const page = () => document.querySelector('[data-work-board-page]');
    const message = text => { const node = page()?.querySelector('[data-board-message]'); if (node) node.textContent = text; };
    async function refresh() {
        // Existing clients may still have the previous cache-first service worker installed.
        const url = new URL(location.href);
        url.searchParams.set('_wpRefresh', Date.now().toString());
        const response = await fetch(url, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok || response.redirected) throw new Error('Không tải được dữ liệu. Hãy tải lại trang hoặc đăng nhập lại.');
        const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
        const replacement = doc.querySelector('[data-work-board-page]');
        if (!replacement) throw new Error('Không tải được bảng công việc.');
        const board = page()?.querySelector('[data-board]');
        const x = board?.scrollLeft || 0;
        page()?.replaceWith(replacement);
        replacement.querySelector('[data-board]').scrollLeft = x;
    }
    async function move(card, target) {
        const original = card.closest('[data-status-id]');
        const select = card.querySelector('[data-card-status]');
        if (pending || !original || original.dataset.statusId === target) { if (select) select.value = original?.dataset.statusId; return; }
        const column = page()?.querySelector(`[data-status-id="${CSS.escape(target)}"]`);
        if (!column || column.dataset.active !== 'true') { if (select) select.value = original.dataset.statusId; return; }
        pending = true;
        page().setAttribute('aria-busy', 'true');
        page().querySelectorAll('[data-card-status]').forEach(s => s.disabled = true);
        const next = card.nextSibling;
        column.querySelector('.wp-cards').append(card);
        message('Đang lưu trạng thái…');
        let saved = false;
        try {
            const form = page().querySelector('[data-move-form]');
            const data = new FormData(form);
            data.set('Id', card.dataset.id); data.set('StatusId', target); data.set('Version', card.dataset.version);
            const response = await fetch(form.action, { method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) {
                const body = await response.json().catch(() => ({}));
                throw new Error(body.message || 'Không thể cập nhật. Kiểm tra quyền hoặc tải lại dữ liệu.');
            }
            saved = true;
            await refresh();
            page()?.querySelector(`[data-card][data-id="${CSS.escape(card.dataset.id)}"] [data-card-status]`)?.focus({ preventScroll: true });
            message('Đã cập nhật trạng thái.');
        } catch (error) {
            if (!saved) { original.querySelector('.wp-cards').insertBefore(card, next); if (select) select.value = original.dataset.statusId; }
            message(saved ? 'Đã lưu nhưng chưa tải được bảng mới. Nhấn Làm mới trước khi thao tác tiếp.' : error.message);
        } finally {
            pending = false;
            page()?.removeAttribute('aria-busy');
            page()?.querySelectorAll('[data-card-status]').forEach(s => s.disabled = false);
        }
    }
    document.addEventListener('dragstart', event => {
        const card = event.target.closest('[data-card]');
        if (!card || card.getAttribute('draggable') !== 'true' || pending) { if (card) event.preventDefault(); return; }
        dragged = card; event.dataTransfer.setData('text/plain', card.dataset.id); event.dataTransfer.effectAllowed = 'move';
    });
    document.addEventListener('dragend', () => { dragged = null; });
    document.addEventListener('dragover', event => { if (dragged && event.target.closest('[data-active="true"]')) event.preventDefault(); });
    document.addEventListener('drop', event => { const column = event.target.closest('[data-status-id]'); if (dragged && column) { event.preventDefault(); void move(dragged, column.dataset.statusId); dragged = null; } });
    document.addEventListener('change', event => { if (event.target.matches('[data-card-status]')) void move(event.target.closest('[data-card]'), event.target.value); });
    document.addEventListener('click', async event => {
        if (!event.target.closest('[data-board-refresh]') || pending) return;
        pending = true; message('Đang tải dữ liệu…');
        try { await refresh(); message('Đã làm mới.'); } catch (error) { message(error.message); } finally { pending = false; }
    });
})();

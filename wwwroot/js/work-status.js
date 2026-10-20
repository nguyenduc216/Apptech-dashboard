(() => {
    document.querySelectorAll('.wp-status-form').forEach(form => {
        const picker = form.querySelector('[data-color-picker]'), hex = form.querySelector('[data-color-hex]');
        const preview = form.querySelector('[data-status-preview]'), name = form.querySelector('[data-status-name]');
        const update = () => {
            preview.textContent = name.value || 'Trạng thái mới';
            if (!/^#[0-9a-f]{6}$/i.test(hex.value)) return;
            picker.value = hex.value; preview.style.backgroundColor = hex.value;
            const rgb = [1, 3, 5].map(i => parseInt(hex.value.slice(i, i + 2), 16) / 255).map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4);
            preview.style.color = rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722 > .179 ? '#111827' : '#ffffff';
        };
        picker.addEventListener('input', () => { hex.value = picker.value; update(); });
        hex.addEventListener('input', update); name.addEventListener('input', update);
        form.addEventListener('submit', () => form.querySelectorAll('button').forEach(b => b.disabled = true));
    });
})();

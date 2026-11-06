// Ô nhập giá theo nghìn đồng (_PriceInput): gõ 90 → hiện "= 90.000 đ". Quy tắc giống MoneyFormat.ExpandThousands ở server:
// số từ 1.000 trở lên coi như đã là đồng. Server vẫn là nơi chốt giá trị cuối cùng.
(function () {
    var fmt = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });

    function parse(text) {
        var t = String(text || '').trim().replace(/\s/g, '');
        if (!t) return null;
        // "90.000" / "1.250.000" (dấu chấm ngăn nghìn) → bỏ chấm; "92.5" hoặc "92,5" → số lẻ.
        if (/^\d{1,3}(\.\d{3})+$/.test(t)) t = t.replace(/\./g, '');
        t = t.replace(',', '.');
        var n = Number(t);
        return isFinite(n) ? n : null;
    }

    function refresh(input) {
        var box = document.querySelector('[data-price-preview="' + input.name + '"]');
        if (!box) return;
        var n = parse(input.value);
        if (n === null) { box.textContent = 'Gõ theo nghìn đồng: 90 = 90.000 đ'; return; }
        var vnd = n > 0 && n < 1000 ? n * 1000 : n;
        box.textContent = '= ' + fmt.format(vnd) + ' đ';
    }

    document.querySelectorAll('[data-price-input]').forEach(function (input) {
        input.addEventListener('input', function () { refresh(input); });
        refresh(input);
    });
})();

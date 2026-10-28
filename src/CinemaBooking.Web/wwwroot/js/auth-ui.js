// Tương tác nhỏ cho trang đăng nhập / đăng ký:
// - Ẩn/hiện mật khẩu.
// - Thanh đo độ mạnh mật khẩu (chỉ có ở trang đăng ký).
(function () {
    'use strict';

    document.querySelectorAll('[data-password-toggle]').forEach(function (btn) {
        var input = document.getElementById(btn.getAttribute('data-password-toggle'));
        if (!input) return;

        btn.addEventListener('click', function () {
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.textContent = show ? '🙈' : '👁️';
            btn.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
            input.focus();
        });
    });

    var password = document.querySelector('[data-password-input]');
    var strength = document.querySelector('[data-password-strength]');
    if (!password || !strength) return;

    var bar = strength.querySelector('[data-password-bar]');
    var label = strength.querySelector('[data-password-label]');

    var levels = [
        { text: 'Rất yếu', color: '#fb7185', width: '20%' },
        { text: 'Yếu', color: '#f59e0b', width: '40%' },
        { text: 'Trung bình', color: '#eab308', width: '60%' },
        { text: 'Mạnh', color: '#34d399', width: '80%' },
        { text: 'Rất mạnh', color: '#10b981', width: '100%' }
    ];

    function score(value) {
        var points = 0;
        if (value.length >= 8) points++;
        if (/[A-Z]/.test(value)) points++;
        if (/[0-9]/.test(value)) points++;
        if (/[^A-Za-z0-9]/.test(value)) points++;
        return Math.min(points, 4);
    }

    password.addEventListener('input', function () {
        var value = password.value;
        if (!value) {
            strength.hidden = true;
            bar.style.width = '0%';
            label.textContent = '';
            return;
        }

        strength.hidden = false;
        var level = levels[score(value)];
        bar.style.width = level.width;
        bar.style.background = level.color;
        label.textContent = 'Độ mạnh: ' + level.text;
        label.style.color = level.color;
    });
})();

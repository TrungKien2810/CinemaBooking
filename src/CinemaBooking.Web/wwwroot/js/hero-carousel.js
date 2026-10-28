// Banner poster phim ở hero trang chủ.
// Tự chuyển ảnh sau 4 giây; dừng khi rê chuột/focus và tôn trọng reduced motion.
(function () {
    'use strict';

    var root = document.querySelector('[data-hero-carousel]');
    if (!root) return;

    var slides = Array.prototype.slice.call(root.querySelectorAll('.hero-slide'));
    var dots = Array.prototype.slice.call(root.querySelectorAll('[data-hero-dot]'));
    if (slides.length < 2) return;

    var index = 0;
    var timer = null;
    var reduced = window.matchMedia('(prefers-reduced-motion: reduce)');

    function show(next) {
        index = (next + slides.length) % slides.length;

        slides.forEach(function (slide, i) {
            var active = i === index;
            slide.classList.toggle('is-active', active);
            slide.setAttribute('aria-hidden', active ? 'false' : 'true');
            slide.tabIndex = active ? 0 : -1;
        });

        dots.forEach(function (dot, i) {
            dot.classList.toggle('is-active', i === index);
            dot.setAttribute('aria-current', i === index ? 'true' : 'false');
        });
    }

    function stop() {
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
    }

    function start() {
        stop();
        if (reduced.matches) return;
        timer = setInterval(function () { show(index + 1); }, 4000);
    }

    dots.forEach(function (dot, i) {
        dot.addEventListener('click', function () {
            show(i);
            start();
        });
    });

    root.addEventListener('mouseenter', stop);
    root.addEventListener('mouseleave', start);
    root.addEventListener('focusin', stop);
    root.addEventListener('focusout', function (event) {
        if (!root.contains(event.relatedTarget)) start();
    });

    root.addEventListener('keydown', function (event) {
        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            show(index - 1);
            start();
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            show(index + 1);
            start();
        }
    });

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) stop();
        else start();
    });

    if (typeof reduced.addEventListener === 'function') {
        reduced.addEventListener('change', start);
    }

    start();
})();

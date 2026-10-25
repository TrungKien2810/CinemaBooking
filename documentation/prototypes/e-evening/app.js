/* ==========================================================================
   E — BUỔI TỐI / app.js
   Static behaviour only: filter films, filter showtimes, pick seats with a
   5-minute hold, validate the checkout form. No build step, no network.
   ========================================================================== */

(() => {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
  const money = (n) => n.toLocaleString('vi-VN') + ' ₫';
  const pad = (n) => String(n).padStart(2, '0');

  /* ------------------------------------------------------------- home --- */

  const filmList = $('#films');
  if (filmList) {
    const search = $('#q');
    const chips = $$('.chip');
    const count = $('#catalog-count');
    const empty = $('#films-empty');
    const rows = $$('.film', filmList);
    let genre = 'all';

    const applyFilters = () => {
      const term = search.value.trim().toLowerCase();
      let shown = 0;
      rows.forEach((row) => {
        const okGenre = genre === 'all' || row.dataset.genre === genre;
        const okTerm = !term || row.dataset.title.includes(term);
        const visible = okGenre && okTerm;
        row.hidden = !visible;
        if (visible) shown += 1;
      });
      count.textContent = shown + ' phim';
      empty.hidden = shown !== 0;
    };

    chips.forEach((chip) => {
      chip.addEventListener('click', () => {
        genre = chip.dataset.genre;
        chips.forEach((other) => other.setAttribute('aria-pressed', String(other === chip)));
        applyFilters();
      });
    });

    search.addEventListener('input', applyFilters);
    applyFilters();
  }

  /* ------------------------------------------------------------ movie --- */

  const axis = $('.axis');
  const nightDate = $('#night-date');
  const dateButtons = $$('.date');

  if (dateButtons.length) {
    dateButtons.forEach((button) => {
      button.addEventListener('click', () => {
        dateButtons.forEach((other) => other.setAttribute('aria-selected', String(other === button)));
        if (nightDate) nightDate.textContent = button.dataset.date;
        if (axis) axis.classList.toggle('axis--now', button.dataset.index === '0');
      });
    });
  }

  const citySelect = $('#city');
  const branchSelect = $('#branch');
  if (citySelect && branchSelect) {
    const lanes = $$('.lane');
    const dayRows = $$('.daytable tbody tr');
    const cityOf = (id) => {
      const lane = lanes.find((el) => el.dataset.branch === id);
      return lane ? lane.dataset.city : 'all';
    };

    const applyBranchFilter = () => {
      const city = citySelect.value;
      const branch = branchSelect.value;
      const show = (el) => {
        const ok = (city === 'all' || el.dataset.city === city) &&
          (branch === 'all' || el.dataset.branch === branch);
        el.hidden = !ok;
        return ok;
      };
      lanes.forEach(show);
      dayRows.forEach(show);
    };

    branchSelect.addEventListener('change', () => {
      if (branchSelect.value !== 'all') citySelect.value = cityOf(branchSelect.value);
      applyBranchFilter();
    });

    citySelect.addEventListener('change', () => {
      if (branchSelect.value !== 'all' && cityOf(branchSelect.value) !== citySelect.value) {
        branchSelect.value = 'all';
      }
      applyBranchFilter();
    });

    applyBranchFilter();
  }

  /* ---------------------------------------------------------- seatmap --- */

  const seatButtons = $$('.seat[data-seat]');
  if (seatButtons.length) {
    const clockEl = $('#clock');
    const clockBox = $('.clock');
    const pickedEl = $('#picked');
    const totalEl = $('#total');
    const cta = $('#to-checkout');
    const message = $('#seat-msg');
    const simulate = $('#simulate-expiry');

    const HOLD_SECONDS = 300;
    let remaining = HOLD_SECONDS;
    let endsAt = 0;
    let timer = null;

    const selectedSeats = () => seatButtons.filter((seat) => seat.getAttribute('aria-pressed') === 'true');

    const paintClock = () => {
      clockEl.textContent = `${pad(Math.floor(remaining / 60))}:${pad(remaining % 60)}`;
      clockBox.classList.toggle('is-urgent', remaining <= 60 && remaining > 0);
    };

    const paintSummary = () => {
      const picked = selectedSeats();
      const total = picked.reduce((sum, seat) => sum + Number(seat.dataset.price), 0);
      pickedEl.textContent = picked.length ? picked.map((seat) => seat.dataset.seat).join(', ') : 'Chưa chọn ghế nào';
      totalEl.textContent = money(total);
      cta.disabled = picked.length === 0;
    };

    const stopTimer = (reset) => {
      if (timer) {
        clearInterval(timer);
        timer = null;
      }
      if (reset) {
        remaining = HOLD_SECONDS;
        paintClock();
        clockBox.classList.remove('is-urgent');
      }
    };

    const expire = () => {
      stopTimer(false);
      remaining = 0;
      paintClock();
      clockBox.classList.remove('is-urgent');
      seatButtons.forEach((seat) => seat.setAttribute('aria-pressed', 'false'));
      paintSummary();
      message.textContent = 'Đã hết 5 phút giữ ghế. Ghế bạn chọn đã được trả lại, mời chọn lại.';
    };

    const startTimer = (seconds) => {
      stopTimer(false);
      remaining = seconds;
      endsAt = Date.now() + seconds * 1000;
      paintClock();
      timer = window.setInterval(() => {
        remaining = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
        paintClock();
        if (remaining === 0) expire();
      }, 200);
    };

    seatButtons.forEach((seat) => {
      seat.addEventListener('click', () => {
        const on = seat.getAttribute('aria-pressed') === 'true';
        seat.setAttribute('aria-pressed', String(!on));
        message.textContent = '';

        if (selectedSeats().length === 0) {
          stopTimer(true);
        } else if (!timer) {
          startTimer(remaining);
        }
        paintSummary();
      });
    });

    cta.addEventListener('click', () => {
      if (cta.disabled) return;
      const codes = selectedSeats().map((seat) => seat.dataset.seat);
      window.location.href = 'checkout.html?seats=' + encodeURIComponent(codes.join(','));
    });

    if (simulate) {
      simulate.addEventListener('click', () => {
        if (selectedSeats().length === 0) {
          const demo = seatButtons.filter((seat) => ['C3', 'C4'].includes(seat.dataset.seat));
          demo.forEach((seat) => seat.setAttribute('aria-pressed', 'true'));
          paintSummary();
        }
        startTimer(3);
      });
    }

    paintClock();
    paintSummary();
  }

  /* --------------------------------------------------------- checkout --- */

  const form = $('#pay-form');
  if (form) {
    const PRICE_BY_ROW = { A: 95000, B: 95000, C: 95000, G: 95000, D: 114000, E: 114000, F: 114000, H: 190000 };
    const TYPE_BY_ROW = { A: 'Thường', B: 'Thường', C: 'Thường', G: 'Thường', D: 'VIP', E: 'VIP', F: 'VIP', H: 'Đôi' };

    const fromUrl = new URLSearchParams(window.location.search).get('seats') || 'C3,C4';
    const codes = fromUrl.split(',')
      .map((code) => code.trim().toUpperCase())
      .filter((code) => /^[A-H]([1-9]|1[0-2])$/.test(code));
    const seats = codes.length ? codes : ['C3', 'C4'];
    const total = seats.reduce((sum, code) => sum + (PRICE_BY_ROW[code[0]] || 95000), 0);

    $('#order-seats').textContent = seats.join(', ');
    $('#order-total').textContent = money(total);

    const types = [...new Set(seats.map((code) => TYPE_BY_ROW[code[0]]))].join(', ');
    $('#order-types').textContent = `${types}, ${money(PRICE_BY_ROW[seats[0][0]] || 95000)} mỗi ghế`;

    const payButton = $('#pay-btn');
    const payLabel = 'Thanh toán ' + money(total);
    payButton.textContent = payLabel;

    const banner = $('#server-error');

    const rules = [
      { input: $('#name'), error: $('#name-error'), valid: (v) => v.trim().length >= 2 },
      { input: $('#email'), error: $('#email-error'), valid: (v) => /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v.trim()) },
      { input: $('#phone'), error: $('#phone-error'), valid: (v) => /^0\d{9}$/.test(v.trim().replace(/[\s.-]/g, '')) },
    ];

    const setFieldState = (rule, ok) => {
      const field = rule.input.closest('.field');
      field.classList.toggle('has-error', !ok);
      rule.input.setAttribute('aria-invalid', String(!ok));
      rule.error.hidden = ok;
    };

    rules.forEach((rule) => {
      rule.input.addEventListener('input', () => {
        banner.hidden = true;
        if (rule.input.getAttribute('aria-invalid') === 'true') setFieldState(rule, true);
      });
    });

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      banner.hidden = true;

      let firstBad = null;
      rules.forEach((rule) => {
        const ok = rule.valid(rule.input.value);
        setFieldState(rule, ok);
        if (!ok && !firstBad) firstBad = rule.input;
      });

      if (firstBad) {
        firstBad.focus();
        return;
      }

      payButton.disabled = true;
      payButton.textContent = 'Đang xử lý…';

      window.setTimeout(() => {
        payButton.disabled = false;
        payButton.textContent = payLabel;
        banner.hidden = false;
        banner.focus();
      }, 1200);
    });
  }
})();

/* ==========================================================================
   A — MARQUEE / prototype behaviour
   Chọn ghế, đồng hồ giữ ghế 5:00, tổng tiền, kiểm tra form,
   mô phỏng hết thời gian giữ ghế, điều hướng nhẹ giữa 4 màn hình.
   Không thư viện ngoài, chạy trực tiếp từ file:// .
   ========================================================================== */
(function () {
  'use strict';

  var doc = document;
  function $(sel, root) { return (root || doc).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || doc).querySelectorAll(sel)); }

  var nf = new Intl.NumberFormat('vi-VN');
  function money(n) { return nf.format(n) + ' ₫'; }
  function pad(n) { return (n < 10 ? '0' : '') + n; }

  /* ---------- Trạng thái đặt vé dùng chung giữa các màn hình ---------- */
  var KEY = 'cinemasmart.marquee.booking';
  function loadBooking() {
    try { return JSON.parse(sessionStorage.getItem(KEY)) || null; } catch (e) { return null; }
  }
  function saveBooking(data) {
    try { sessionStorage.setItem(KEY, JSON.stringify(data)); } catch (e) { /* bản demo, bỏ qua */ }
  }

  /* ======================================================================
     1. TRANG CHỦ — tìm kiếm và lọc thể loại
     ====================================================================== */
  function initHome() {
    var search = $('#search-film');
    var chips = $$('.chip');
    var cards = $$('.film-card');
    var counter = $('#result-count');
    var empty = $('#film-empty');
    var reset = $('#reset-filters');
    if (!cards.length) { return; }

    var state = { q: '', genre: 'all' };

    function apply() {
      var shown = 0;
      cards.forEach(function (card) {
        var title = (card.dataset.title || '').toLowerCase();
        var genre = card.dataset.genre || '';
        var okGenre = state.genre === 'all' || genre === state.genre;
        var okQuery = !state.q || title.indexOf(state.q) !== -1;
        var visible = okGenre && okQuery;
        card.hidden = !visible;
        if (visible) { shown += 1; }
      });
      if (counter) { counter.textContent = 'Hiển thị ' + shown + ' phim'; }
      if (empty) { empty.hidden = shown !== 0; }
    }

    if (search) {
      search.addEventListener('input', function () {
        state.q = search.value.trim().toLowerCase();
        apply();
      });
    }

    chips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        chips.forEach(function (other) { other.setAttribute('aria-pressed', 'false'); });
        chip.setAttribute('aria-pressed', 'true');
        state.genre = chip.dataset.genre || 'all';
        apply();
      });
    });

    if (reset) {
      reset.addEventListener('click', function () {
        state.q = '';
        state.genre = 'all';
        if (search) { search.value = ''; }
        chips.forEach(function (other) {
          other.setAttribute('aria-pressed', other.dataset.genre === 'all' ? 'true' : 'false');
        });
        apply();
      });
    }
  }

  /* ======================================================================
     2. CHI TIẾT PHIM — dải ngày, lọc thành phố và chi nhánh
     ====================================================================== */
  function initMovie() {
    var strip = $('#day-strip');
    var groups = $$('.branch-group');
    var citySel = $('#city');
    var branchSel = $('#branch');
    var note = $('#showtime-note');
    var emptyFilter = $('#empty-filter');

    var DOW = ['Chủ nhật', 'Thứ hai', 'Thứ ba', 'Thứ tư', 'Thứ năm', 'Thứ sáu', 'Thứ bảy'];
    var DOW_SHORT = ['CN', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];

    if (strip) {
      var today = new Date();
      for (var i = 0; i < 7; i += 1) {
        var d = new Date(today.getFullYear(), today.getMonth(), today.getDate() + i);
        var btn = doc.createElement('button');
        btn.type = 'button';
        btn.className = 'day';
        btn.setAttribute('aria-pressed', i === 0 ? 'true' : 'false');
        btn.setAttribute('aria-label', 'Lịch chiếu ngày ' + d.getDate() + ' tháng ' + (d.getMonth() + 1) + ', ' + DOW[d.getDay()]);
        btn.innerHTML = '<span class="day__dow">' + (i === 0 ? 'Hôm nay' : DOW_SHORT[d.getDay()]) + '</span>' +
                        '<span class="day__num">' + d.getDate() + '</span>';
        btn.addEventListener('click', function (e) {
          $$('.day', strip).forEach(function (other) { other.setAttribute('aria-pressed', 'false'); });
          e.currentTarget.setAttribute('aria-pressed', 'true');
          if (note) {
            note.textContent = 'Lịch chiếu ngày ' + d.getDate() + ' tháng ' + (d.getMonth() + 1) + ' năm ' + d.getFullYear();
          }
        });
        strip.appendChild(btn);
      }
    }

    function branchesOf(city) {
      return groups.filter(function (g) { return city === 'all' || g.dataset.city === city; });
    }

    function fillBranches() {
      if (!branchSel) { return; }
      var city = citySel ? citySel.value : 'all';
      var list = branchesOf(city);
      branchSel.innerHTML = '<option value="all">Tất cả chi nhánh</option>';
      list.forEach(function (g) {
        var opt = doc.createElement('option');
        opt.value = g.dataset.branch;
        opt.textContent = g.querySelector('.branch-group__name').textContent;
        branchSel.appendChild(opt);
      });
    }

    function applyFilters() {
      var city = citySel && citySel.value ? citySel.value : 'all';
      var branch = branchSel && branchSel.value ? branchSel.value : 'all';
      var shown = 0;
      groups.forEach(function (g) {
        var okCity = city === 'all' || g.dataset.city === city;
        var okBranch = branch === 'all' || g.dataset.branch === branch;
        g.hidden = !(okCity && okBranch);
        if (!g.hidden) { shown += 1; }
      });
      if (emptyFilter) { emptyFilter.hidden = shown !== 0; }
    }

    if (citySel) {
      citySel.addEventListener('change', function () {
        fillBranches();
        applyFilters();
      });
    }
    if (branchSel) {
      branchSel.addEventListener('change', applyFilters);
    }
    fillBranches();
    applyFilters();
  }

  /* ======================================================================
     3. SƠ ĐỒ GHẾ
     ====================================================================== */
  var ROWS = [
    { id: 'A', type: 'normal' },
    { id: 'B', type: 'normal' },
    { id: 'C', type: 'vip' },
    { id: 'D', type: 'vip' },
    { id: 'E', type: 'vip' },
    { id: 'F', type: 'normal' },
    { id: 'G', type: 'normal' },
    { id: 'H', type: 'couple' }
  ];
  var PRICE = { normal: 95000, vip: 114000, couple: 190000 };
  var TYPE_NAME = { normal: 'ghế thường', vip: 'ghế VIP', couple: 'ghế đôi' };
  var BOOKED = ['B2', 'B3', 'F5', 'F6', 'G7'];
  var HELD = ['D7', 'D8'];
  var MINE = ['C3', 'C4'];
  var HOLD_SECONDS = 300;

  function buildSeats() {
    var seats = [];
    ROWS.forEach(function (row) {
      if (row.type === 'couple') {
        for (var c = 1; c <= 12; c += 2) {
          seats.push({
            id: row.id + c,
            code: row.id + c + '–' + row.id + (c + 1),
            row: row.id, col: c, type: 'couple', price: PRICE.couple
          });
        }
      } else {
        for (var i = 1; i <= 12; i += 1) {
          seats.push({
            id: row.id + i,
            code: row.id + i,
            row: row.id, col: i, type: row.type, price: PRICE[row.type]
          });
        }
      }
    });
    return seats;
  }

  function initSeatMap() {
    var host = $('#seat-rows');
    if (!host) { return; }

    var seats = buildSeats();
    var selected = MINE.slice();
    var list = $('#seat-chips');
    var total = $('#seat-total');
    var count = $('#seat-count');
    var cta = $('#cta-checkout');
    var clock = $('#hold-clock');
    var timerBox = $('#hold-timer');
    var alert = $('#seat-alert');
    var clearBtn = $('#clear-seats');
    var expireBtn = $('#simulate-expire');
    var left = HOLD_SECONDS;
    var ticker = null;

    function seatById(id) {
      for (var i = 0; i < seats.length; i += 1) { if (seats[i].id === id) { return seats[i]; } }
      return null;
    }

    function labelOf(seat, state) {
      var text = 'Ghế ' + seat.code + ', ' + TYPE_NAME[seat.type] + ', ' + money(seat.price) + ', ';
      if (state === 'free') { return text + 'còn trống, bấm để chọn'; }
      if (state === 'mine') { return text + 'bạn đang giữ, bấm để bỏ chọn'; }
      if (state === 'held') { return text + 'người khác đang giữ, không chọn được'; }
      return text + 'đã đặt, không chọn được';
    }

    function renderSeats() {
      var html = '';
      ROWS.forEach(function (row) {
        html += '<div class="seat-row"><span class="seat-row__label">' + row.id + '</span><div class="seat-row__seats">';
        seats.forEach(function (seat) {
          if (seat.row !== row.id) { return; }
          var state = 'free';
          if (BOOKED.indexOf(seat.id) !== -1) { state = 'booked'; }
          else if (HELD.indexOf(seat.id) !== -1) { state = 'held'; }
          else if (selected.indexOf(seat.id) !== -1) { state = 'mine'; }
          html += '<button type="button" class="seat" data-id="' + seat.id + '" data-code="' + seat.code +
            '" data-state="' + state + '" data-type="' + seat.type + '" data-col="' + seat.col +
            '" data-price="' + seat.price + '" aria-pressed="' + (state === 'mine' ? 'true' : 'false') + '"' +
            (state === 'booked' || state === 'held' ? ' aria-disabled="true"' : '') +
            ' aria-label="' + labelOf(seat, state) + '" title="' + seat.code + '">' + seat.code + '</button>';
        });
        html += '</div></div>';
      });
      host.innerHTML = html;
    }

    function renderSummary() {
      var chosen = selected.map(seatById).filter(Boolean);
      var sum = chosen.reduce(function (acc, seat) { return acc + seat.price; }, 0);

      if (list) {
        list.innerHTML = chosen.length
          ? chosen.map(function (seat) {
              return '<span class="seat-picked__item"><span class="seat-picked__code">' + seat.code + '</span>' +
                     '<span class="seat-picked__type">' + TYPE_NAME[seat.type] + '</span>' +
                     '<span class="seat-picked__price">' + money(seat.price) + '</span></span>';
            }).join('')
          : '<span class="seat-picked__empty">Chưa chọn ghế nào. Bấm vào ghế trống trên sơ đồ.</span>';
      }
      if (total) { total.textContent = money(sum); }
      if (count) { count.textContent = chosen.length + ' ghế'; }
      if (cta) {
        cta.disabled = chosen.length === 0;
        cta.setAttribute('aria-disabled', chosen.length === 0 ? 'true' : 'false');
      }
      saveBooking({
        seats: chosen.map(function (s) { return { code: s.code, type: TYPE_NAME[s.type], price: s.price }; }),
        total: sum,
        count: chosen.length
      });
    }

    function paintClock() {
      if (!clock) { return; }
      clock.textContent = pad(Math.floor(left / 60)) + ':' + pad(left % 60);
      if (timerBox) { timerBox.classList.toggle('is-urgent', left <= 60); }
    }

    function expire() {
      selected = [];
      renderSeats();
      renderSummary();
      paintClock();
      if (timerBox) { timerBox.classList.add('is-urgent'); }
      if (alert) {
        alert.hidden = false;
        alert.textContent = 'Đã hết 5 phút giữ ghế. Toàn bộ ghế bạn chọn được trả về rạp, hãy chọn lại để tiếp tục.';
      }
      if (host) { host.setAttribute('aria-describedby', 'seat-alert'); }
    }

    function startTimer() {
      paintClock();
      ticker = setInterval(function () {
        left -= 1;
        if (left <= 0) { clearInterval(ticker); ticker = null; expire(); return; }
        paintClock();
      }, 1000);
    }

    host.addEventListener('click', function (e) {
      var btn = e.target.closest ? e.target.closest('.seat') : null;
      if (!btn || btn.getAttribute('aria-disabled') === 'true') { return; }
      var id = btn.dataset.id;
      if (selected.indexOf(id) === -1) {
        selected.push(id);
      } else {
        selected = selected.filter(function (x) { return x !== id; });
      }
      if (alert) { alert.hidden = true; }
      renderSeats();
      renderSummary();
    });

    if (clearBtn) {
      clearBtn.addEventListener('click', function () {
        selected = [];
        if (alert) { alert.hidden = true; }
        renderSeats();
        renderSummary();
      });
    }

    if (cta) {
      cta.addEventListener('click', function () {
        if (selected.length === 0) { return; }
        window.location.href = 'checkout.html';
      });
    }

    if (expireBtn) {
      expireBtn.addEventListener('click', function () {
        if (ticker) { clearInterval(ticker); ticker = null; }
        left = 0;
        expire();
      });
    }

    renderSeats();
    renderSummary();
    startTimer();
  }

  /* ======================================================================
     4. THANH TOÁN — kiểm tra form, trạng thái đang xử lý, lỗi máy chủ
     ====================================================================== */
  function initCheckout() {
    var form = $('#checkout-form');
    if (!form) { return; }

    var summarySeat = $('#order-seats');
    var summaryTotal = $('#order-total');
    var summaryCount = $('#order-count');
    var submit = $('#submit-order');
    var alertBox = $('#form-alert');
    var alertText = $('#form-alert-text');
    var serverAlert = $('#server-alert');
    var done = $('#order-done');
    var orderCode = $('#order-code');

    var booking = loadBooking();
    if (booking && summarySeat) {
      summarySeat.textContent = booking.seats.length
        ? 'Ghế ' + booking.seats.map(function (s) { return s.code; }).join(', ')
        : 'Chưa có ghế nào được giữ';
      if (summaryTotal) { summaryTotal.textContent = money(booking.total); }
      if (summaryCount) { summaryCount.textContent = booking.count + ' ghế'; }
    }

    var RULES = [
      {
        id: 'fullname',
        test: function (v) { return v.trim().length >= 2; },
        empty: 'Vui lòng nhập họ và tên.',
        wrong: 'Họ và tên cần ít nhất 2 ký tự.'
      },
      {
        id: 'email',
        test: function (v) { return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v.trim()); },
        empty: 'Vui lòng nhập email để nhận vé.',
        wrong: 'Email chưa đúng định dạng, ví dụ ban@example.com.'
      },
      {
        id: 'phone',
        test: function (v) { return /^0\d{9}$/.test(v.trim()); },
        empty: 'Vui lòng nhập số điện thoại.',
        wrong: 'Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.'
      }
    ];

    function validateField(rule) {
      var input = $('#' + rule.id);
      var field = input.closest('.field');
      var error = $('#' + rule.id + '-error');
      var value = input.value;
      var ok = rule.test(value);
      if (ok) {
        field.classList.remove('is-invalid');
        input.removeAttribute('aria-invalid');
        error.textContent = '';
        error.hidden = true;
      } else {
        field.classList.add('is-invalid');
        input.setAttribute('aria-invalid', 'true');
        error.textContent = value.trim() === '' ? rule.empty : rule.wrong;
        error.hidden = false;
      }
      return ok;
    }

    RULES.forEach(function (rule) {
      var input = $('#' + rule.id);
      input.addEventListener('blur', function () { validateField(rule); });
      input.addEventListener('input', function () {
        var field = input.closest('.field');
        if (field.classList.contains('is-invalid')) { validateField(rule); }
      });
    });

    $$('.pay-option input').forEach(function (radio) {
      radio.addEventListener('change', function () {
        $$('.pay-option').forEach(function (opt) { opt.classList.remove('is-checked'); });
        radio.closest('.pay-option').classList.add('is-checked');
      });
    });
    var checked = $('input[name="payment"]:checked');
    if (checked) { checked.closest('.pay-option').classList.add('is-checked'); }

    var simulate = $('#simulate-server-error');
    if (simulate) {
      simulate.addEventListener('click', function () {
        if (done) { done.hidden = true; }
        if (serverAlert) { serverAlert.hidden = false; }
        if (alertBox) { alertBox.hidden = true; }
        serverAlert.focus && serverAlert.focus();
      });
    }

    form.addEventListener('submit', function (e) {
      e.preventDefault();
      if (serverAlert) { serverAlert.hidden = true; }
      if (done) { done.hidden = true; }

      var firstBad = null;
      RULES.forEach(function (rule) {
        var ok = validateField(rule);
        if (!ok && !firstBad) { firstBad = rule.id; }
      });

      if (firstBad) {
        if (alertBox) {
          alertBox.hidden = false;
          if (alertText) { alertText.textContent = 'Có ' + RULES.length + ' trường cần sửa, ô đã báo đỏ là ô cần kiểm tra lại.'; }
        }
        $('#' + firstBad).focus();
        return;
      }

      if (alertBox) { alertBox.hidden = true; }
      submit.classList.add('is-busy');
      submit.disabled = true;
      submit.textContent = 'Đang xử lý';

      setTimeout(function () {
        submit.classList.remove('is-busy');
        submit.disabled = false;
        submit.textContent = 'Xác nhận đặt vé';
        if (orderCode) {
          var code = '';
          var chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
          for (var i = 0; i < 6; i += 1) { code += chars.charAt(Math.floor(Math.random() * chars.length)); }
          orderCode.textContent = 'CS-' + code;
        }
        if (done) {
          done.hidden = false;
          done.focus();
        }
      }, 1200);
    });
  }

  /* ======================================================================
     Khởi động
     ====================================================================== */
  function boot() {
    initHome();
    initMovie();
    initSeatMap();
    initCheckout();
  }

  if (doc.readyState === 'loading') {
    doc.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();

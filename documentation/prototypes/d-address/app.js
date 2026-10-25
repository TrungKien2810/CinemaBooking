/* ==========================================================================
   d-address / app.js — tĩnh, không phụ thuộc mạng, chạy bằng file://
   Trạng thái cửa rạp: ?branch= -> sessionStorage -> CineSmart Nguyễn Huệ
   ========================================================================== */
(function () {
  'use strict';

  /* --- Dữ liệu (đúng CatalogSeeder) ------------------------------------ */
  var BRANCHES = {
    'nguyen-hue': {
      name: 'CineSmart Nguyễn Huệ', addr: '72 Nguyễn Huệ, Quận 1',
      city: 'TP. Hồ Chí Minh', cityKey: 'hcm', base: 95000, rooms: [['Phòng 1', 8, 12], ['Phòng 2', 6, 10]]
    },
    'thao-dien': {
      name: 'CineSmart Thảo Điền', addr: '12 Quốc Hương, Thảo Điền',
      city: 'TP. Hồ Chí Minh', cityKey: 'hcm', base: 85000, rooms: [['Phòng 1', 7, 10]]
    },
    'cau-giay': {
      name: 'CineSmart Cầu Giấy', addr: '15 Trần Duy Hưng',
      city: 'Hà Nội', cityKey: 'hanoi', base: 90000, rooms: [['Phòng A', 10, 14], ['Phòng B', 5, 8]]
    },
    'hoan-kiem': {
      name: 'CineSmart Hoàn Kiếm', addr: '8 Tràng Tiền',
      city: 'Hà Nội', cityKey: 'hanoi', base: 80000, rooms: [['Phòng 1', 6, 9]]
    }
  };

  var NEXT = {
    'nguyen-hue': { t: '18:45', room: 'Phòng 1', left: 42 },
    'thao-dien': { t: '14:30', room: 'Phòng 1', left: 18 },
    'cau-giay': { t: '19:15', room: 'Phòng A', left: 12 },
    'hoan-kiem': { t: '20:30', room: 'Phòng 1', left: 9 }
  };

  var KEY_BRANCH = 'cinebooking.d-address.branch';
  var KEY_BOOKING = 'cinebooking.d-address.booking';

  var DAYS = ['21/10', '22/10', '23/10', '24/10', '25/10', '26/10', '27/10'];
  var FACTOR = [1, 0.92, 0.8, 0.86, 0.64, 0.46, 0.32];

  var FIXTURE_MINE = ['C3', 'C4'];
  var FIXTURE_HELD = ['D7', 'D8'];
  var FIXTURE_BOOKED = ['B2', 'B3', 'F5', 'F6', 'G7'];
  var ROWS = 'ABCDEFGHIJ';

  var page = document.body.getAttribute('data-page') || '';
  var params = new URLSearchParams(window.location.search);

  /* --- Tiện ích --------------------------------------------------------- */
  function store(key, value) {
    try {
      if (value === undefined) return window.sessionStorage.getItem(key);
      window.sessionStorage.setItem(key, value);
    } catch (e) { /* file:// vẫn chạy, chỉ mất ghi nhớ */ }
    return null;
  }

  function fmt(amount) {
    return amount.toLocaleString('vi-VN') + ' ₫';
  }

  function norm(text) {
    return text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().trim();
  }

  function branchId() {
    var id = params.get('branch') || store(KEY_BRANCH) || 'nguyen-hue';
    return BRANCHES[id] ? id : 'nguyen-hue';
  }

  function setBranch(id) {
    if (!BRANCHES[id]) return;
    store(KEY_BRANCH, id);
    syncBranch(id);
  }

  function syncBranch(id) {
    var b = BRANCHES[id];
    Array.prototype.forEach.call(document.querySelectorAll('[data-branch-name]'), function (el) {
      el.textContent = b.name;
    });
  }

  function readBooking() {
    var raw = store(KEY_BOOKING);
    if (!raw) return null;
    try { return JSON.parse(raw); } catch (e) { return null; }
  }

  function writeBooking(data) {
    store(KEY_BOOKING, JSON.stringify(data));
  }

  /* ====================================================================== */
  /* TRANG CHỦ                                                              */
  /* ====================================================================== */
  function initHome() {
    var doors = Array.prototype.slice.call(document.querySelectorAll('.door'));
    var tabs = Array.prototype.slice.call(document.querySelectorAll('.genres .tab'));
    var search = document.querySelector('[data-search]');
    var rows = Array.prototype.slice.call(document.querySelectorAll('.film'));
    var empty = document.getElementById('film-empty');
    var listState = document.getElementById('list-state');
    var featureNext = document.getElementById('feature-next');
    var state = { branch: branchId(), genre: 'all', q: '' };

    function applyFilters() {
      var b = BRANCHES[state.branch];
      var shown = 0;
      var soon = 0;

      rows.forEach(function (row) {
        var okBranch = (row.getAttribute('data-branches') || '').split(' ').indexOf(state.branch) !== -1;
        var name = row.querySelector('.film__name').textContent;
        var genre = row.querySelector('.film__genre').textContent;
        var okGenre = state.genre === 'all' || genre === state.genre;
        var okQuery = !state.q || norm(name).indexOf(norm(state.q)) !== -1;
        var ok = okBranch && okGenre && okQuery;

        row.hidden = !ok;
        if (ok) {
          shown += 1;
          if (row.querySelector('.film__status--soon')) soon += 1;
          row.setAttribute('href', 'movie.html?branch=' + state.branch);
        }
      });

      empty.hidden = shown !== 0;
      listState.textContent = shown === 0
        ? 'Không có phim nào khớp bộ lọc. Giá vé ở rạp này từ ' + fmt(b.base) + '.'
        : shown + ' phim ở rạp này' +
          (soon ? ', ' + soon + ' phim sắp chiếu' : '') +
          '. Giá vé từ ' + fmt(b.base) + '.';

      Array.prototype.forEach.call(document.querySelectorAll('[data-price]'), function (el) {
        el.textContent = fmt(b.base);
      });

      var n = NEXT[state.branch];
      featureNext.textContent = 'Suất gần nhất ở ' + b.name + ': ' + n.t + ', ' + n.room.toLowerCase() + ', còn ' + n.left + ' ghế.';
    }

    doors.forEach(function (door) {
      door.addEventListener('click', function () {
        state.branch = door.getAttribute('data-branch');
        store(KEY_BRANCH, state.branch);
        doors.forEach(function (d) {
          d.setAttribute('aria-pressed', String(d === door));
        });
        syncBranch(state.branch);
        applyFilters();
      });
    });

    tabs.forEach(function (tab) {
      tab.addEventListener('click', function () {
        state.genre = tab.getAttribute('data-genre');
        tabs.forEach(function (t) {
          t.setAttribute('aria-pressed', String(t === tab));
        });
        applyFilters();
      });
    });

    if (search) {
      search.addEventListener('input', function () {
        state.q = search.value;
        applyFilters();
      });
    }

    doors.forEach(function (d) {
      d.setAttribute('aria-pressed', String(d.getAttribute('data-branch') === state.branch));
    });
    syncBranch(state.branch);
    applyFilters();
  }

  /* ====================================================================== */
  /* TRANG PHIM                                                             */
  /* ====================================================================== */
  function initMovie() {
    var dates = Array.prototype.slice.call(document.querySelectorAll('.date'));
    var cityBtns = Array.prototype.slice.call(document.querySelectorAll('.cities__tabs [data-city]'));
    var cards = Array.prototype.slice.call(document.querySelectorAll('.branch'));
    var slots = Array.prototype.slice.call(document.querySelectorAll('.slot'));
    var stateEl = document.getElementById('st-state');
    var cityState = document.getElementById('city-state');
    var current = branchId();
    var state = { day: 0, city: '' };

    slots.forEach(function (s) {
      s.setAttribute('data-base', s.getAttribute('data-left'));
      s.addEventListener('click', function () {
        var card = s.closest('.branch');
        var query = '?branch=' + card.getAttribute('data-branch') +
          '&time=' + encodeURIComponent(s.getAttribute('data-time')) +
          '&room=' + encodeURIComponent(s.getAttribute('data-room')) +
          '&date=' + encodeURIComponent(DAYS[state.day]);
        window.location.href = 'seatmap.html' + query;
      });
    });

    cards.forEach(function (card) {
      var isCurrent = card.getAttribute('data-branch') === current;
      card.classList.toggle('branch--current', isCurrent);
      var here = card.querySelector('.branch__here');
      if (here) here.hidden = !isCurrent;
    });
    syncBranch(current);

    function applyDay() {
      slots.forEach(function (s) {
        var base = Number(s.getAttribute('data-base'));
        var left = base === 0 ? 0 : Math.round(base * FACTOR[state.day]);
        if (base > 0 && left < 5) left = 0;

        s.setAttribute('data-left', String(left));
        s.classList.toggle('slot--out', left === 0);
        s.disabled = left === 0;
        s.querySelector('.slot__left').textContent = left === 0 ? 'hết chỗ' : 'còn ' + left + ' ghế';
      });
      updateState();
    }

    function applyCity() {
      var visible = 0;
      cards.forEach(function (card) {
        var show = !state.city || card.getAttribute('data-city') === state.city;
        card.hidden = !show;
        if (show) visible += 1;
      });
      cityBtns.forEach(function (b) {
        b.setAttribute('aria-pressed', String(b.getAttribute('data-city') === state.city));
      });
      cityState.textContent = state.city
        ? 'Đang lọc ' + (state.city === 'hcm' ? 'TP. Hồ Chí Minh' : 'Hà Nội') + ': ' + visible + ' chi nhánh. Bấm lại để xem cả 4.'
        : 'Đang hiện cả 4 chi nhánh.';
      updateState();
    }

    function updateState() {
      var city = state.city === 'hcm' ? 'TP. Hồ Chí Minh' : state.city === 'hanoi' ? 'Hà Nội' : 'cả 4 chi nhánh';
      stateEl.textContent = 'Ngày ' + DAYS[state.day] + ', ' + city + '.';
    }

    dates.forEach(function (d) {
      d.addEventListener('click', function () {
        state.day = Number(d.getAttribute('data-day'));
        dates.forEach(function (x) {
          x.setAttribute('aria-pressed', String(x === d));
        });
        applyDay();
      });
    });

    cityBtns.forEach(function (b) {
      b.addEventListener('click', function () {
        var city = b.getAttribute('data-city');
        state.city = state.city === city ? '' : city;
        applyCity();
      });
    });

    applyDay();
    applyCity();
  }

  /* ====================================================================== */
  /* CHỌN GHẾ                                                               */
  /* ====================================================================== */
  function seatTypeName(type) {
    return type === 'vip' ? 'VIP' : type === 'couple' ? 'đôi' : 'thường';
  }

  function initSeatmap() {
    var id = branchId();
    var b = BRANCHES[id];
    var room = b.rooms[0];
    var roomName = params.get('room') || room[0];
    var time = params.get('time') || '19:15';
    var date = params.get('date') || DAYS[1];
    var rowsCount = room[1];
    var colsCount = room[2];
    var prices = { std: b.base, vip: Math.round(b.base * 1.2), couple: b.base * 2 };

    /* Đầu trang: đúng một dòng địa chỉ của suất đang mở. */
    var venue = document.querySelector('.seat-head__venue');
    if (venue) {
      venue.textContent = b.name + ' · ' + roomName + ' · ' + rowsCount + '×' + colsCount + ' · ' + time;
    }
    store(KEY_BRANCH, id);
    syncBranch(id);

    var summaryDate = document.getElementById('summary-date');
    if (summaryDate) summaryDate.textContent = date + ', ' + time;

    var legendPrices = document.querySelectorAll('[data-legend-price]');
    Array.prototype.forEach.call(legendPrices, function (el) {
      el.textContent = fmt(prices[el.getAttribute('data-legend-price')]);
    });

    var grid = document.getElementById('seatgrid');
    var totalEl = document.getElementById('total');
    var pickedEl = document.getElementById('picked');
    var cta = document.getElementById('to-checkout');
    var msg = document.getElementById('expire-msg');
    var clock = document.getElementById('clock');
    var simulate = document.getElementById('simulate');
    var seats = [];
    var selected = [];

    var saved = readBooking();
    if (saved && saved.branch === id && saved.room === roomName) {
      selected = saved.seats || [];
    } else {
      selected = FIXTURE_MINE.filter(function (code) {
        return ROWS.indexOf(code.charAt(0)) < rowsCount && Number(code.slice(1)) <= colsCount;
      });
    }

    function priceOf(type) {
      return prices[type === 'vip' ? 'vip' : type === 'couple' ? 'couple' : 'std'];
    }

    function typeOf(rowIndex) {
      if (rowIndex === rowsCount - 1) return 'couple';
      if (rowIndex >= rowsCount / 3 && rowIndex < (2 * rowsCount) / 3) return 'vip';
      return 'std';
    }

    var aisleAfter = Math.floor(colsCount / 2);

    for (var r = 0; r < rowsCount; r += 1) {
      var type = typeOf(r);
      var rowEl = document.createElement('div');
      rowEl.className = 'seatrow';
      rowEl.setAttribute('role', 'group');
      rowEl.setAttribute('aria-label', 'Hàng ' + ROWS.charAt(r) + ', ghế ' + seatTypeName(type));

      var label = document.createElement('span');
      label.className = 'seatrow__label';
      label.setAttribute('aria-hidden', 'true');
      label.textContent = ROWS.charAt(r);

      var cells = document.createElement('span');
      cells.className = 'seatrow__cells';

      for (var c = 1; c <= colsCount; c += 1) {
        var code = ROWS.charAt(r) + c;
        var stateName = 'còn trống';
        var isLocked = false;

        if (FIXTURE_BOOKED.indexOf(code) !== -1) { stateName = 'đã bán'; isLocked = true; }
        else if (FIXTURE_HELD.indexOf(code) !== -1) { stateName = 'người khác đang giữ'; isLocked = true; }

        var seat = document.createElement('button');
        seat.type = 'button';
        seat.className = 'seat seat--' + type;
        seat.setAttribute('data-code', code);
        seat.setAttribute('data-type', type);
        seat.textContent = code;

        if (isLocked) {
          seat.disabled = true;
          seat.className += stateName === 'đã bán' ? ' seat--booked' : ' seat--held';
          seat.setAttribute('aria-label', code + ', ghế ' + seatTypeName(type) + ', ' + fmt(priceOf(type)) + ', ' + stateName);
        } else {
          var pressed = selected.indexOf(code) !== -1;
          seat.setAttribute('aria-pressed', String(pressed));
          seat.setAttribute('aria-label', code + ', ghế ' + seatTypeName(type) + ', ' + fmt(priceOf(type)) + ', ' + (pressed ? 'bạn đang giữ' : stateName));
          seats.push(seat);
        }

        cells.appendChild(seat);

        if (c === aisleAfter) {
          var aisle = document.createElement('span');
          aisle.className = 'seatrow__aisle';
          aisle.setAttribute('aria-hidden', 'true');
          cells.appendChild(aisle);
        }
      }

      rowEl.appendChild(label);
      rowEl.appendChild(cells);
      grid.appendChild(rowEl);
    }

    function total() {
      return selected.reduce(function (sum, code) {
        var seat = grid.querySelector('[data-code="' + code + '"]');
        return sum + (seat ? priceOf(seat.getAttribute('data-type')) : 0);
      }, 0);
    }

    function render() {
      var sum = total();
      totalEl.textContent = fmt(sum);
      pickedEl.textContent = selected.length ? selected.join(', ') : 'Chưa chọn ghế';

      if (selected.length) {
        cta.removeAttribute('aria-disabled');
        cta.setAttribute('href', 'checkout.html?branch=' + id);
      } else {
        cta.setAttribute('aria-disabled', 'true');
        cta.removeAttribute('href');
      }

      writeBooking({
        branch: id, room: roomName, time: time, date: date,
        seats: selected.slice(), total: sum, film: 'Dune: Part Two'
      });
    }

    function toggle(seat) {
      var code = seat.getAttribute('data-code');
      var at = selected.indexOf(code);
      if (at === -1) {
        selected.push(code);
        seat.setAttribute('aria-pressed', 'true');
        seat.setAttribute('aria-label', code + ', ghế ' + seatTypeName(seat.getAttribute('data-type')) + ', ' + fmt(priceOf(seat.getAttribute('data-type'))) + ', bạn đang giữ');
      } else {
        selected.splice(at, 1);
        seat.setAttribute('aria-pressed', 'false');
        seat.setAttribute('aria-label', code + ', ghế ' + seatTypeName(seat.getAttribute('data-type')) + ', ' + fmt(priceOf(seat.getAttribute('data-type'))) + ', còn trống');
      }
      if (msg) msg.hidden = true;
      render();
    }

    seats.forEach(function (seat) {
      seat.addEventListener('click', function () { toggle(seat); });
    });

    if (cta) {
      cta.addEventListener('click', function (event) {
        if (cta.getAttribute('aria-disabled') === 'true') event.preventDefault();
      });
    }

    /* Đồng hồ giữ ghế 5:00 */
    var left = 300;
    var timer = null;

    function paintClock() {
      var m = Math.floor(left / 60);
      var s = left % 60;
      clock.textContent = (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
    }

    function expire() {
      window.clearInterval(timer);
      timer = null;
      selected = [];
      seats.forEach(function (seat) {
        seat.setAttribute('aria-pressed', 'false');
      });
      render();
      clock.textContent = '00:00';
      if (msg) msg.hidden = false;
      if (cta) cta.setAttribute('aria-disabled', 'true');
    }

    function tick() {
      left -= 1;
      if (left <= 0) {
        left = 0;
        paintClock();
        expire();
        return;
      }
      paintClock();
    }

    paintClock();
    timer = window.setInterval(tick, 1000);

    if (simulate) {
      simulate.addEventListener('click', function () {
        left = 1;
        tick();
      });
    }

    render();
  }

  /* ====================================================================== */
  /* THANH TOÁN                                                             */
  /* ====================================================================== */
  function initCheckout() {
    var booking = readBooking();
    var b = BRANCHES[booking && BRANCHES[booking.branch] ? booking.branch : 'nguyen-hue'];
    var id = booking ? booking.branch : 'nguyen-hue';
    var seats = booking && booking.seats && booking.seats.length ? booking.seats : ['C3', 'C4'];
    var totalAmount = booking && booking.total ? booking.total : 190000;

    store(KEY_BRANCH, id);
    syncBranch(id);

    var addrEl = document.querySelector('[data-order-addr]');
    if (addrEl) addrEl.textContent = b.addr + ', ' + b.city;

    var whereEl = document.querySelector('[data-order-where]');
    if (whereEl) whereEl.textContent = '— ' + (booking ? booking.room : 'Phòng 1') + ', ' + (booking ? booking.time : '19:15');

    var dateEl = document.querySelector('[data-order-date]');
    if (dateEl) dateEl.textContent = booking ? booking.date : '22/10';

    var seatsEl = document.querySelector('[data-order-seats]');
    if (seatsEl) seatsEl.textContent = seats.join(', ');

    var totalEl = document.querySelector('[data-order-total]');
    if (totalEl) totalEl.textContent = fmt(totalAmount);

    var submit = document.getElementById('pay-submit');
    if (submit) submit.textContent = 'Thanh toán ' + fmt(totalAmount);

    var form = document.getElementById('pay-form');
    var banner = document.getElementById('server-error');

    var RULES = [
      {
        id: 'name', err: 'name-err',
        test: function (v) { return v.trim().length >= 2; },
        message: 'Vui lòng nhập họ và tên đầy đủ.'
      },
      {
        id: 'email', err: 'email-err',
        test: function (v) { return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v.trim()); },
        message: 'Email chưa đúng định dạng. Ví dụ: an@example.com'
      },
      {
        id: 'phone', err: 'phone-err',
        test: function (v) { return /^0\d{9}$/.test(v.replace(/[\s.\-]/g, '')); },
        message: 'Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.'
      }
    ];

    function fieldOf(rule) {
      return document.getElementById(rule.id);
    }

    function showError(rule, message) {
      var input = fieldOf(rule);
      var box = document.getElementById(rule.err);
      if (!input || !box) return;
      input.setAttribute('aria-invalid', 'true');
      box.textContent = message;
      box.hidden = false;
    }

    function clearError(rule) {
      var input = fieldOf(rule);
      var box = document.getElementById(rule.err);
      if (!input || !box) return;
      input.removeAttribute('aria-invalid');
      box.hidden = true;
      box.textContent = '';
    }

    RULES.forEach(function (rule) {
      var input = fieldOf(rule);
      if (!input) return;
      input.addEventListener('input', function () { clearError(rule); });
    });

    if (!form) return;

    form.addEventListener('submit', function (event) {
      event.preventDefault();
      if (banner) banner.hidden = true;

      var firstBad = null;
      RULES.forEach(function (rule) {
        var input = fieldOf(rule);
        if (!input) return;
        if (!rule.test(input.value)) {
          showError(rule, rule.message);
          if (!firstBad) firstBad = input;
        } else {
          clearError(rule);
        }
      });

      if (firstBad) {
        firstBad.focus();
        return;
      }

      var label = submit.textContent;
      submit.disabled = true;
      submit.setAttribute('aria-busy', 'true');
      submit.textContent = 'Đang xử lý…';

      window.setTimeout(function () {
        submit.disabled = false;
        submit.removeAttribute('aria-busy');
        submit.textContent = label;
        if (banner) {
          banner.hidden = false;
          banner.scrollIntoView({ block: 'nearest' });
        }
      }, 1200);
    });
  }

  /* --- Khởi động -------------------------------------------------------- */
  if (page === 'home') initHome();
  if (page === 'movie') initMovie();
  if (page === 'seatmap') initSeatmap();
  if (page === 'checkout') initCheckout();
  if (page === 'index') syncBranch(branchId());
}());

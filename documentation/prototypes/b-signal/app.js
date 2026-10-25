/* ==========================================================================
   B — Signal · app.js
   Tĩnh, không thư viện ngoài. Chạy trên file:// .
   - Dữ liệu demo lấy từ CatalogSeeder.cs (xem CONTENT-SPEC.md)
   - Mỗi màn hình tự khởi tạo nếu có phần tử tương ứng trong DOM.
   ========================================================================== */
(function () {
  'use strict';

  /* ------------------------------------------------------------ helpers */
  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  function vnd(n) {
    return new Intl.NumberFormat('vi-VN').format(n) + ' ₫';
  }
  function pad2(n) { return (n < 10 ? '0' : '') + n; }

  /* Ngưỡng khan hiếm dùng chung cho cả thanh tín hiệu lẫn số ghế còn lại.
     open = còn nhiều, tight = còn ít, last = sắp hết, none = hết chỗ. */
  function scarcity(free, total) {
    if (!free) return 'none';
    var r = free / total;
    if (r >= 0.40) return 'open';
    if (r >= 0.15) return 'tight';
    return 'last';
  }

  /* --------------------------------------------------------------- data */
  var MOVIES = [
    { id: 'mat-biec', title: 'Mắt Biếc', genre: 'Tình cảm', minutes: 117,
      release: '20/12/2019', from: 80000, status: 'now',
      blurb: 'Chuyện tình đơn phương của Ngạn dành cho Hà Lan, từ làng Đo Đo lên thành phố.' },
    { id: 'nha-ba-nu', title: 'Nhà Bà Nữ', genre: 'Hài', minutes: 106,
      release: '22/01/2023', from: 80000, status: 'now',
      blurb: 'Bi hài kịch gia đình ba thế hệ quanh một quán bánh canh nhỏ.' },
    { id: 'dune-2', title: 'Dune: Part Two', genre: 'Khoa học viễn tưởng', minutes: 166,
      release: '01/03/2024', from: 80000, status: 'now',
      blurb: 'Paul Atreides liên minh với người Fremen để trả thù những kẻ đã hủy diệt gia tộc mình.' },
    { id: 'inside-out-2', title: 'Inside Out 2', genre: 'Hoạt hình', minutes: 96,
      release: '14/06/2024', from: 80000, status: 'now',
      blurb: 'Riley bước vào tuổi dậy thì và bộ não cô bé đón thêm những cảm xúc mới.' },
    { id: 'dia-dao', title: 'Địa Đạo: Mặt Trời Trong Bóng Tối', genre: 'Chiến tranh', minutes: 128,
      release: '04/04/2025', from: 80000, status: 'soon',
      blurb: 'Một đội du kích bám trụ trong hệ thống địa đạo giữa những ngày ác liệt nhất.' }
  ];

  var GENRES = ['Tình cảm', 'Hài', 'Khoa học viễn tưởng', 'Hoạt hình', 'Chiến tranh'];

  var BRANCHES = [
    { id: 'nguyen-hue', name: 'CineSmart Nguyễn Huệ', city: 'TP. Hồ Chí Minh', addr: '72 Nguyễn Huệ, Quận 1', base: 95000 },
    { id: 'thao-dien', name: 'CineSmart Thảo Điền', city: 'TP. Hồ Chí Minh', addr: '12 Quốc Hương, Thảo Điền', base: 85000 },
    { id: 'cau-giay', name: 'CineSmart Cầu Giấy', city: 'Hà Nội', addr: '15 Trần Duy Hưng', base: 90000 },
    { id: 'hoan-kiem', name: 'CineSmart Hoàn Kiếm', city: 'Hà Nội', addr: '8 Tràng Tiền', base: 80000 }
  ];

  /* Suất chiếu của Dune: Part Two — ngày 21/04/2025 (hôm nay của demo).
     free = số ghế còn trống, cap = số ghế trong phòng. */
  var SHOWTIMES = [
    { branch: 'nguyen-hue', time: '09:00', room: 'Phòng 1', cap: 96, free: 62 },
    { branch: 'nguyen-hue', time: '12:15', room: 'Phòng 2', cap: 60, free: 38 },
    { branch: 'nguyen-hue', time: '15:30', room: 'Phòng 1', cap: 96, free: 24 },
    { branch: 'nguyen-hue', time: '18:45', room: 'Phòng 1', cap: 96, free: 43 },
    { branch: 'nguyen-hue', time: '22:00', room: 'Phòng 2', cap: 60, free: 0 },
    { branch: 'thao-dien', time: '10:30', room: 'Phòng 1', cap: 70, free: 18 },
    { branch: 'thao-dien', time: '14:30', room: 'Phòng 1', cap: 70, free: 27 },
    { branch: 'thao-dien', time: '19:00', room: 'Phòng 1', cap: 70, free: 6 },
    { branch: 'cau-giay', time: '09:30', room: 'Phòng A', cap: 140, free: 88 },
    { branch: 'cau-giay', time: '12:45', room: 'Phòng B', cap: 40, free: 21 },
    { branch: 'cau-giay', time: '16:00', room: 'Phòng A', cap: 140, free: 34 },
    { branch: 'cau-giay', time: '19:15', room: 'Phòng A', cap: 140, free: 9 },
    { branch: 'cau-giay', time: '22:30', room: 'Phòng B', cap: 40, free: 0 },
    { branch: 'hoan-kiem', time: '11:00', room: 'Phòng 1', cap: 54, free: 41 },
    { branch: 'hoan-kiem', time: '17:00', room: 'Phòng 1', cap: 54, free: 16 },
    { branch: 'hoan-kiem', time: '20:30', room: 'Phòng 1', cap: 54, free: 5 }
  ];

  /* Sơ đồ ghế — CineSmart Nguyễn Huệ, Phòng 1 (8 hàng × 12 cột), suất 18:45.
     '.' = trống, '#' = đã đặt, 'H' = người khác đang giữ, 'M' = bạn đang giữ. */
  var ROOM = {
    rows: 8,
    cols: 12,
    seats: {
      A: { type: 'standard', map: '#.#.#.#.#.#' },
      B: { type: 'standard', map: '.##.##.###.' },
      C: { type: 'vip',      map: '##MM...#.#.' },
      D: { type: 'vip',      map: '######HH####' },
      E: { type: 'vip',      map: '.#.#.#.#.#..' },
      F: { type: 'standard', map: '#.####.###..' },
      G: { type: 'standard', map: '.#.#.##..#..' },
      H: { type: 'couple',   map: '..#.#..#....' }
    }
  };
  var SEAT_PRICE = { standard: 95000, vip: 114000, couple: 190000 };
  var SEAT_TYPE_NAME = { standard: 'ghế thường', vip: 'ghế VIP', couple: 'ghế đôi' };

  var SHOW = {
    movie: 'Dune: Part Two',
    movieId: 'dune-2',
    branch: 'CineSmart Nguyễn Huệ',
    branchId: 'nguyen-hue',
    room: 'Phòng 1',
    time: '18:45',
    date: '22/04/2025',
    holdSeconds: 300
  };

  /* ------------------------------------------------------------ storage */
  var memoryStore = null;
  function readStore(key) {
    try {
      var raw = window.sessionStorage.getItem(key);
      if (raw) return JSON.parse(raw);
    } catch (e) { /* file:// chặn storage — dùng bộ nhớ tạm */ }
    return memoryStore ? memoryStore[key] : null;
  }
  function writeStore(key, value) {
    memoryStore = memoryStore || {};
    memoryStore[key] = value;
    try { window.sessionStorage.setItem(key, JSON.stringify(value)); } catch (e) { /* bỏ qua */ }
  }

  /* -------------------------------------------------- điều hướng nhẹ ---- */
  function markCurrentNav() {
    var here = (location.pathname.split('/').pop() || 'index.html').toLowerCase();
    $$('[data-nav]').forEach(function (a) {
      var target = a.getAttribute('href').split('/').pop().toLowerCase();
      if (target === here) a.setAttribute('aria-current', 'page');
    });
  }

  /* ================================================== TRANG CHỦ ====== */
  function initHome() {
    var grid = $('#movieGrid');
    if (!grid) return;

    var state = { q: '', genre: '', status: '' };

    function cardHtml(m) {
      var level = scarcity(m.status === 'now' ? 42 : 0, 96);
      var pulse = m.status === 'now'
        ? '<span class="avail" data-level="' + level + '">' +
            '<span class="avail__count">42</span><span>ghế còn trống</span></span>'
        : '<span class="avail" data-level="none"><span class="avail__count">—</span><span>suất ra mắt tuần sau</span></span>';
      var statusTag = m.status === 'now'
        ? '<span class="tag tag--live">Đang chiếu</span>'
        : '<span class="tag tag--soon">Sắp chiếu</span>';
      var poster = '<div class="poster poster--' + posterIndex(m.id) + '" aria-hidden="true">' +
          '<span class="poster__genre">' + m.genre + '</span>' +
          '<span class="poster__title">' + m.title + '</span>' +
          '<span class="poster__foot">' +
            '<span class="poster__minutes">' + m.minutes + '&nbsp;phút</span>' +
            '<span class="meter">' + meterHtml() + '</span>' +
          '</span></div>';
      return '<article class="movie-card">' +
          '<a class="movie-card__link" href="movie.html">' + poster + '</a>' +
          '<div class="movie-card__title">' + m.title + '</div>' +
          '<div class="meta-row"><span class="meta-item"><span class="k">Thể loại</span><span class="v">' + m.genre + '</span></span>' +
          '<span class="meta-item"><span class="k">Thời lượng</span><span class="v">' + m.minutes + '&nbsp;phút</span></span></div>' +
          '<div class="movie-card__foot">' + pulse + '<span class="rail-item__price">từ ' + vnd(m.from) + '</span></div>' +
          '<div>' + statusTag + '</div>' +
        '</article>';
    }

    function posterIndex(id) {
      var i = MOVIES.map(function (m) { return m.id; }).indexOf(id);
      return i + 1;
    }
    function meterHtml() {
      var out = '';
      for (var i = 0; i < 7; i++) out += '<span></span>';
      return out;
    }

    function render() {
      var list = MOVIES.filter(function (m) {
        if (state.status === 'now' && m.status !== 'now') return false;
        if (state.status === 'soon' && m.status !== 'soon') return false;
        if (state.genre && m.genre !== state.genre) return false;
        if (state.q) {
          var q = state.q.toLowerCase();
          if (m.title.toLowerCase().indexOf(q) === -1 && m.genre.toLowerCase().indexOf(q) === -1) return false;
        }
        return true;
      });
      var count = $('#resultCount');
      if (count) count.textContent = list.length + '/' + MOVIES.length + ' phim';
      grid.innerHTML = list.length
        ? list.map(cardHtml).join('')
        : '<p class="empty-note">Không có phim nào khớp bộ lọc hiện tại. Xoá từ khoá hoặc bỏ chọn thể loại để xem lại toàn bộ danh mục.</p>';
    }

    $$('#q, #qFilter').forEach(function (input) {
      input.addEventListener('input', function () {
        state.q = input.value.trim();
        $$('#q, #qFilter').forEach(function (other) {
          if (other !== input) other.value = input.value;
        });
        render();
      });
    });
    $$('[data-filter="genre"]').forEach(function (chip) {
      chip.addEventListener('click', function () {
        var v = chip.getAttribute('data-value');
        state.genre = state.genre === v ? '' : v;
        $$('[data-filter="genre"]').forEach(function (c) {
          c.setAttribute('aria-pressed', String(c.getAttribute('data-value') === state.genre));
        });
        render();
      });
    });
    $$('[data-filter="status"]').forEach(function (chip) {
      chip.addEventListener('click', function () {
        var v = chip.getAttribute('data-value');
        state.status = state.status === v ? '' : v;
        $$('[data-filter="status"]').forEach(function (c) {
          c.setAttribute('aria-pressed', String(c.getAttribute('data-value') === state.status));
        });
        render();
      });
    });

    render();
  }

  /* ================================================ CHI TIẾT PHIM ====== */
  function initMovie() {
    var host = $('#schedule');
    if (!host) return;

    var city = '', branch = '';

    function branchById(id) {
      for (var i = 0; i < BRANCHES.length; i++) if (BRANCHES[i].id === id) return BRANCHES[i];
      return null;
    }

    function render() {
      var list = BRANCHES.filter(function (b) {
        if (city && b.city !== city) return false;
        if (branch && b.id !== branch) return false;
        return true;
      });
      var total = 0;
      var html = list.map(function (b) {
        var slots = SHOWTIMES.filter(function (s) { return s.branch === b.id; });
        total += slots.length;
        var inner = slots.map(function (s) {
          var level = scarcity(s.free, s.cap);
          var full = s.free === 0;
          var price = b.base;
          var pulse = '<span class="pulse" data-level="' + level + '" aria-hidden="true">' +
              '<span class="pulse__fill" style="--pct:' + Math.round((s.free / s.cap) * 100) + '%"></span></span>';
          var avail = full
            ? '<span class="avail" data-level="none"><span class="avail__count">0</span><span>hết chỗ</span></span>'
            : '<span class="avail" data-level="' + level + '"><span class="avail__count">' + s.free + '</span><span>ghế còn trống</span></span>';
          return '<a class="slot' + (full ? ' slot--full' : '') + '" href="seatmap.html"' +
              (full ? ' aria-disabled="true" tabindex="-1"' : '') + '>' +
              '<span class="slot__time">' + s.time + '</span>' +
              '<span class="slot__room">' + s.room + '</span>' +
              '<span class="slot__price">' + vnd(price) + '</span>' +
              '<span class="slot__pulse">' + pulse + '</span>' +
              '<span class="slot__avail">' + avail + '</span>' +
            '</a>';
        }).join('');

        return '<section class="sched-group">' +
            '<div>' +
              '<h3 class="sched-branch__name">' + b.name + '</h3>' +
              '<p class="sched-branch__addr">' + b.addr + '</p>' +
              '<p class="sched-branch__price">Giá ghế thường ' + vnd(b.base) + ' · VIP ' + vnd(Math.round(b.base * 1.2)) + ' · đôi ' + vnd(b.base * 2) + '</p>' +
            '</div>' +
            '<div class="sched-slots">' + inner + '</div>' +
          '</section>';
      }).join('');

      host.innerHTML = html;
      var count = $('#slotCount');
      if (count) count.textContent = total + ' suất chiếu';
    }

    $$('.day').forEach(function (day) {
      day.addEventListener('click', function () {
        $$('.day').forEach(function (d) { d.setAttribute('aria-pressed', String(d === day)); });
      });
    });

    host.addEventListener('click', function (e) {
      var slot = e.target.closest ? e.target.closest('.slot') : null;
      if (slot && slot.classList.contains('slot--full')) e.preventDefault();
    });

    $$('[data-filter="city"]').forEach(function (chip) {
      chip.addEventListener('click', function () {
        var v = chip.getAttribute('data-value');
        city = city === v ? '' : v;
        if (city) branch = '';
        $$('[data-filter="city"]').forEach(function (c) {
          c.setAttribute('aria-pressed', String(c.getAttribute('data-value') === city));
        });
        $$('[data-filter="branch"]').forEach(function (c) {
          c.setAttribute('aria-pressed', String(c.getAttribute('data-value') === branch));
        });
        render();
      });
    });
    $$('[data-filter="branch"]').forEach(function (chip) {
      chip.addEventListener('click', function () {
        var v = chip.getAttribute('data-value');
        branch = branch === v ? '' : v;
        $$('[data-filter="branch"]').forEach(function (c) {
          c.setAttribute('aria-pressed', String(c.getAttribute('data-value') === branch));
        });
        render();
      });
    });

    render();
  }

  /* ===================================================== CHỌN GHẾ ====== */
  function initSeatMap() {
    var grid = $('#seatGrid');
    if (!grid) return;

    var mine = [];      /* ghế bạn đang giữ */
    var expiry = null;  /* mốc hết hạn (ms) */

    var seatInfo = {};
    var freeByRow = {};
    var freeTotal = 0;

    function seatPrice(row) {
      var t = ROOM.seats[row].type;
      return SEAT_PRICE[t];
    }

    function buildGrid() {
      var letters = Object.keys(ROOM.seats);
      var html = '';
      /* lối đi nằm giữa: sau cột 6 của 12 cột */
      var half = Math.floor(ROOM.cols / 2);

      function seatCell(letter, row, i) {
        var ch = row.map.charAt(i);
        var id = letter + (i + 1);
        var state = ch === '#' ? 'booked' : (ch === 'H' ? 'held' : (ch === 'M' ? 'mine' : 'free'));
        var price = seatPrice(letter);
        var typeName = SEAT_TYPE_NAME[row.type];
        var label = 'Ghế ' + id + ', ' + typeName + ', ' + vnd(price) + ', ' +
          (state === 'free' ? 'còn trống' :
           state === 'mine' ? 'bạn đang giữ' :
           state === 'held' ? 'người khác đang giữ' : 'đã đặt');
        var mark = state === 'mine' ? '✓' : (state === 'held' ? '◐' : (state === 'booked' ? '×' : ''));
        var cls = state === 'free' ? 'seat--free' :
          state === 'mine' ? 'seat--mine' :
          state === 'held' ? 'seat--held' : 'seat--booked';
        return '<button type="button" class="seat ' + cls + '" data-seat="' + id + '"' +
            ' data-state="' + state + '" data-type="' + row.type + '" data-price="' + price + '"' +
            ' aria-pressed="' + (state === 'mine' ? 'true' : 'false') + '"' +
            (state === 'free' ? '' : ' aria-disabled="true"') +
            ' aria-label="' + label + '">' +
            '<span class="seat__label">' + id + '</span>' +
            (mark ? '<span class="seat__mark" aria-hidden="true">' + mark + '</span>' : '') +
          '</button>';
      }

      /* hàng đầu: số cột, lối đi đặt đúng chỗ như các hàng ghế */
      html += '<div class="seatmap__rowhead"></div>';
      for (var c = 1; c <= half; c++) html += '<div class="seatmap__colnum">' + c + '</div>';
      html += '<div class="seatmap__aisle"></div>';
      for (var c2 = half + 1; c2 <= ROOM.cols; c2++) html += '<div class="seatmap__colnum">' + c2 + '</div>';
      html += '<div class="seatmap__rowhead">Còn<br>trống</div>';

      letters.forEach(function (letter) {
        var row = ROOM.seats[letter];
        html += '<div class="seatmap__rowlabel">' + letter + '</div>';
        for (var i = 0; i < half; i++) html += seatCell(letter, row, i);
        html += '<div class="seatmap__aisle"></div>';
        for (var j = half; j < ROOM.cols; j++) html += seatCell(letter, row, j);
        html += '<div class="rowpulse" data-row="' + letter + '">' +
            '<span class="rowpulse__count">0</span>' +
            '<span class="pulse" data-level="open" aria-hidden="true"><span class="pulse__fill"></span></span>' +
          '</div>';
      });

      grid.innerHTML = html;

      /* đếm ghế trống theo hàng */
      letters.forEach(function (letter) {
        var row = ROOM.seats[letter];
        var free = 0;
        for (var i = 0; i < ROOM.cols; i++) {
          var ch = row.map.charAt(i);
          if (ch === '.') free++;
          else if (ch === 'M') mine.push(letter + (i + 1));
        }
        freeByRow[letter] = free;
        freeTotal += free;
      });

      $$('.seat', grid).forEach(function (btn) {
        var id = btn.getAttribute('data-seat');
        seatInfo[id] = {
          row: id.charAt(0),
          type: btn.getAttribute('data-type'),
          price: parseInt(btn.getAttribute('data-price'), 10)
        };
      });

      /* cập nhật thanh nhịp theo hàng */
      letters.forEach(function (letter) {
        var free = freeByRow[letter];
        var cell = $('.rowpulse[data-row="' + letter + '"]', grid);
        if (!cell) return;
        var level = scarcity(free, ROOM.cols);
        $('.rowpulse__count', cell).textContent = free;
        var bar = $('.pulse', cell);
        bar.setAttribute('data-level', level);
        $('.pulse__fill', bar).style.setProperty('--pct', Math.round((free / ROOM.cols) * 100) + '%');
      });
    }

    /* ------------------------------------------------------- trạng thái */
    var totalEls = $$('[data-bind="total"]');
    var seatEls = $$('[data-bind="seats"]');
    var listEl = $('#seatList');
    var ctas = $$('#ctaCheckout, #ctaCheckoutMobile');
    var timerEls = $$('[data-bind="timer"]');
    var banner = $('#holdBanner');
    var totalFreeEl = $('[data-bind="free"]');

    function total() {
      return mine.reduce(function (sum, id) { return sum + seatInfo[id].price; }, 0);
    }

    function render() {
      var sum = total();
      totalEls.forEach(function (el) { el.textContent = vnd(sum); });
      seatEls.forEach(function (el) {
        el.textContent = mine.length ? mine.slice().sort().join(', ') : 'chưa chọn ghế nào';
      });
      if (listEl) {
        if (!mine.length) {
          listEl.innerHTML = '<li class="seatline seatline--empty">Chọn ghế trên sơ đồ để xem chi tiết và tổng tiền.</li>';
        } else {
          listEl.innerHTML = mine.slice().sort().map(function (id) {
            var info = seatInfo[id];
            return '<li class="seatline"><span class="seatline__id">' + id + '</span>' +
              '<span class="seatline__type">' + SEAT_TYPE_NAME[info.type] + '</span>' +
              '<span class="seatline__price">' + vnd(info.price) + '</span></li>';
          }).join('');
        }
      }
      var canGo = mine.length > 0 && !expired();
      ctas.forEach(function (el) {
        var mobile = el.id === 'ctaCheckoutMobile';
        if (el.tagName === 'BUTTON') {
          el.disabled = !canGo;
        } else if (canGo) {
          el.removeAttribute('aria-disabled');
          el.classList.remove('is-disabled');
        } else {
          el.setAttribute('aria-disabled', 'true');
          el.classList.add('is-disabled');
        }
        el.textContent = mine.length === 0
          ? (mobile ? 'Chọn ghế' : 'Chọn ghế để tiếp tục')
          : (mobile ? 'Tiếp tục' : 'Tiếp tục thanh toán');
      });
      if (totalFreeEl) totalFreeEl.textContent = freeTotal;
      save();
    }

    function save() {
      writeStore('b-signal-order', {
        seats: mine.slice().sort(),
        total: total(),
        movie: SHOW.movie,
        branch: SHOW.branch,
        room: SHOW.room,
        time: SHOW.time,
        date: SHOW.date,
        expiry: expiry
      });
    }

    function expired() { return expiry !== null && Date.now() >= expiry; }

    /* ---------------------------------------------------------- đồng hồ */
    var tickHandle = null;
    function renderTimer() {
      if (!timerEls.length) return;
      var left = expiry === null ? SHOW.holdSeconds * 1000 : Math.max(0, expiry - Date.now());
      var secs = Math.ceil(left / 1000);
      var m = Math.floor(secs / 60), s = secs % 60;
      timerEls.forEach(function (el) {
        el.textContent = m + ':' + pad2(s);
        var box = el.closest('.timer');
        if (box) box.classList.toggle('is-urgent', secs <= 60);
      });
      var bar = $('.timer .pulse');
      if (bar) {
        var pct = Math.max(0, Math.min(100, (left / (SHOW.holdSeconds * 1000)) * 100));
        $('.pulse__fill', bar).style.setProperty('--pct', pct + '%');
        bar.setAttribute('data-level', secs <= 60 ? 'tight' : 'open');
      }
    }

    function startTimer() {
      if (tickHandle) clearInterval(tickHandle);
      tickHandle = setInterval(function () {
        if (expiry === null) return;
        if (Date.now() >= expiry) { expireHold(); return; }
        renderTimer();
      }, 1000);
    }

    function expireHold() {
      expiry = null;
      mine = [];
      $$('.seat--mine', grid).forEach(function (b) {
        b.classList.remove('seat--mine');
        b.classList.add('seat--free');
        b.setAttribute('data-state', 'free');
        b.setAttribute('aria-pressed', 'false');
        b.setAttribute('aria-label', b.getAttribute('aria-label').replace('bạn đang giữ', 'còn trống'));
        var mark = $('.seat__mark', b);
        if (mark) mark.remove();
      });
      if (banner) {
        banner.hidden = false;
        banner.className = 'banner banner--warn';
        banner.innerHTML = '<span><strong class="banner__title">Hết thời gian giữ ghế.</strong>' +
          'Bạn đã giữ ghế quá 5 phút nên hệ thống tự bỏ chọn. Chọn lại ghế để tiếp tục.</span>';
      }
      render();
    }

    function beginHold() {
      expiry = Date.now() + SHOW.holdSeconds * 1000;
      if (banner) banner.hidden = true;
      renderTimer();
      startTimer();
    }

    /* ---------------------------------------------------------- chọn ghế */
    grid.addEventListener('click', function (e) {
      var btn = e.target.closest ? e.target.closest('.seat') : null;
      if (!btn || btn.getAttribute('aria-disabled') === 'true') return;
      var id = btn.getAttribute('data-seat');
      var idx = mine.indexOf(id);
      if (idx === -1) {
        mine.push(id);
        btn.classList.remove('seat--free');
        btn.classList.add('seat--mine');
        btn.setAttribute('data-state', 'mine');
        btn.setAttribute('aria-pressed', 'true');
        btn.setAttribute('aria-label', btn.getAttribute('aria-label').replace('còn trống', 'bạn đang giữ'));
        if (!$('.seat__mark', btn)) {
          var m = document.createElement('span');
          m.className = 'seat__mark';
          m.setAttribute('aria-hidden', 'true');
          m.textContent = '✓';
          btn.appendChild(m);
        }
        if (expiry === null) beginHold();
      } else {
        mine.splice(idx, 1);
        btn.classList.remove('seat--mine');
        btn.classList.add('seat--free');
        btn.setAttribute('data-state', 'free');
        btn.setAttribute('aria-pressed', 'false');
        btn.setAttribute('aria-label', btn.getAttribute('aria-label').replace('bạn đang giữ', 'còn trống'));
        var mark = $('.seat__mark', btn);
        if (mark) mark.remove();
      }
      render();
    });

    /* -------------------------------------------------------- mô phỏng */
    var sim = $('#simExpire');
    if (sim) {
      sim.addEventListener('click', function () {
        beginHold();
        expiry = Date.now() - 1;
        expireHold();
      });
    }

    var clearAll = $('#clearSeats');
    if (clearAll) {
      clearAll.addEventListener('click', function () {
        mine = [];
        $$('.seat--mine', grid).forEach(function (b) {
          b.classList.remove('seat--mine');
          b.classList.add('seat--free');
          b.setAttribute('data-state', 'free');
          b.setAttribute('aria-pressed', 'false');
          var mark = $('.seat__mark', b);
          if (mark) mark.remove();
        });
        render();
      });
    }

    /* ------------------------------------------------------------ khởi tạo */
    buildGrid();
    var stored = readStore('b-signal-order');
    if (stored && stored.expiry && Date.now() < stored.expiry && stored.seats && stored.seats.length) {
      expiry = stored.expiry;
      mine = [];
      $$('.seat--mine', grid).forEach(function (b) {
        b.classList.remove('seat--mine');
        b.classList.add('seat--free');
        b.setAttribute('data-state', 'free');
        b.setAttribute('aria-pressed', 'false');
        var mk = $('.seat__mark', b);
        if (mk) mk.remove();
      });
      stored.seats.forEach(function (id) {
        var btn = $('.seat[data-seat="' + id + '"]', grid);
        if (!btn || btn.getAttribute('data-state') !== 'free') return;
        mine.push(id);
        btn.classList.remove('seat--free');
        btn.classList.add('seat--mine');
        btn.setAttribute('data-state', 'mine');
        btn.setAttribute('aria-pressed', 'true');
        btn.setAttribute('aria-label', btn.getAttribute('aria-label').replace('còn trống', 'bạn đang giữ'));
        var m = document.createElement('span');
        m.className = 'seat__mark';
        m.setAttribute('aria-hidden', 'true');
        m.textContent = '✓';
        btn.appendChild(m);
      });
    } else {
      beginHold();
    }
    render();
    renderTimer();
    startTimer();
  }

  /* ==================================================== THANH TOÁN ====== */
  function initCheckout() {
    var form = $('#checkoutForm');
    if (!form) return;

    var order = readStore('b-signal-order') || {
      seats: ['C3', 'C4'],
      total: 190000,
      movie: SHOW.movie,
      branch: SHOW.branch,
      room: SHOW.room,
      time: SHOW.time,
      date: SHOW.date
    };

    var seatInfo = {};
    Object.keys(ROOM.seats).forEach(function (letter) {
      var row = ROOM.seats[letter];
      for (var i = 0; i < ROOM.cols; i++) seatInfo[letter + (i + 1)] = { type: row.type, price: SEAT_PRICE[row.type] };
    });

    $$('[data-bind="movie"]').forEach(function (el) { el.textContent = order.movie; });
    $$('[data-bind="branch"]').forEach(function (el) { el.textContent = order.branch; });
    $$('[data-bind="room"]').forEach(function (el) { el.textContent = order.room; });
    $$('[data-bind="time"]').forEach(function (el) { el.textContent = order.time; });
    $$('[data-bind="date"]').forEach(function (el) { el.textContent = order.date; });
    $$('[data-bind="seats"]').forEach(function (el) { el.textContent = order.seats.join(', '); });
    $$('[data-bind="total"]').forEach(function (el) { el.textContent = vnd(order.total); });

    var seatRows = $('#summarySeats');
    if (seatRows) {
      seatRows.innerHTML = order.seats.map(function (id) {
        var info = seatInfo[id] || { type: 'standard', price: 95000 };
        return '<li class="seatline"><span class="seatline__id">' + id + '</span>' +
          '<span class="seatline__type">' + SEAT_TYPE_NAME[info.type] + '</span>' +
          '<span class="seatline__price">' + vnd(info.price) + '</span></li>';
      }).join('');
    }

    /* ------------------------------------------------------- validate --- */
    var RULES = {
      fullName: {
        test: function (v) { return v.trim().length >= 2; },
        msg: 'Nhập họ và tên (tối thiểu 2 ký tự).'
      },
      email: {
        test: function (v) { return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v.trim()); },
        msg: 'Email chưa đúng định dạng, ví dụ nhan@example.com.'
      },
      phone: {
        test: function (v) { return /^(0|\+84)[0-9]{9}$/.test(v.replace(/[\s.\-]/g, '')); },
        msg: 'Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0, ví dụ 0901234567.'
      }
    };

    function validateField(input) {
      var rule = RULES[input.id];
      if (!rule) return true;
      var field = input.closest('.field');
      var error = $('.field-error', field);
      var ok = rule.test(input.value);
      field.classList.toggle('is-invalid', !ok);
      input.setAttribute('aria-invalid', String(!ok));
      if (error) error.textContent = ok ? '' : rule.msg;
      return ok;
    }

    $$('.input', form).forEach(function (input) {
      input.addEventListener('blur', function () { validateField(input); });
      input.addEventListener('input', function () {
        var field = input.closest('.field');
        if (field && field.classList.contains('is-invalid')) validateField(input);
      });
    });

    /* ------------------------------------------------------ thanh toán -- */
    var errorBanner = $('#serverError');
    var successPanel = $('#successPanel');
    var submit = $('#payBtn');
    var outcome = 'ok';

    $$('[data-sim]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        outcome = btn.getAttribute('data-sim');
        $$('[data-sim]').forEach(function (b) { b.setAttribute('aria-pressed', String(b === btn)); });
      });
    });

    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var ok = true;
      $$('.input', form).forEach(function (input) {
        if (!validateField(input)) ok = false;
      });
      if (!ok) {
        var first = $('.field.is-invalid .input', form);
        if (first) first.focus();
        return;
      }
      if (errorBanner) errorBanner.hidden = true;
      submit.disabled = true;
      submit.classList.add('is-loading');
      var old = submit.textContent;
      submit.innerHTML = '<span class="spinner" aria-hidden="true"></span><span>Đang xử lý</span>';
      var done = function () {
        submit.disabled = false;
        submit.classList.remove('is-loading');
        submit.textContent = old;
        if (outcome === 'error') {
          if (errorBanner) {
            errorBanner.hidden = false;
            errorBanner.focus();
          }
          return;
        }
        form.hidden = true;
        if (successPanel) {
          successPanel.hidden = false;
          successPanel.focus();
        }
      };
      setTimeout(done, 1400);
    });

    /* -------------------------------------------------- đổi cách thanh toán */
    $$('.pay-option input[type="radio"]').forEach(function (radio) {
      radio.addEventListener('change', function () {
        $$('.pay-option').forEach(function (label) {
          var input = $('input', label);
          label.classList.toggle('is-checked', input && input.checked);
        });
      });
    });
  }

  /* ----------------------------------------------------------- khởi động */
  document.addEventListener('DOMContentLoaded', function () {
    markCurrentNav();
    initHome();
    initMovie();
    initSeatMap();
    initCheckout();
  });
})();

# ADR 0001 — Lọc catalog bằng SQL, không bằng PLINQ

- **Trạng thái:** Đã chốt
- **Ngày:** 2026-09-14 (Sprint 2)
- **Người đề xuất:** Dev B · **Liên quan:** [`General.md` §7.4](../../General.md), B2.1–B2.3

## Bối cảnh

Bản đặc tả đầu tiên mô tả trang chủ và trang chi tiết phim "lọc phim và lịch chiếu bằng
`AsParallel()` khi truy vấn" — PLINQ được kể như một điểm kỹ thuật của đề tài.

Khi hiện thực B2.1/B2.2, cách viết đó dẫn tới:

```csharp
// Cách viết theo đặc tả ban đầu
var movies = await db.Movies.Where(m => m.IsActive).ToListAsync();
return movies.AsParallel()
    .Where(m => m.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
    .ToList();
```

Hai vấn đề:

1. `AsParallel()` là **LINQ-to-Objects**, không phải LINQ-to-Entities. Gọi trực tiếp trên
   `IQueryable` của EF Core thì hoặc không biên dịch được, hoặc âm thầm chuyển sang đánh giá ở
   client — nghĩa là **kéo cả bảng về RAM rồi mới lọc**.
2. Ngay cả khi `ToListAsync()` trước rồi mới `AsParallel()`, chi phí thật vẫn nằm ở chỗ đọc toàn
   bộ bảng qua dây và dựng entity, không nằm ở vòng lặp lọc. Song song hoá đúng cái phần rẻ nhất.

## Quyết định

**Mọi thao tác lọc, sắp xếp, DISTINCT và phân trang của catalog đều nằm trong `IQueryable`
và được EF Core dịch thành một câu SQL duy nhất.** `AsParallel()` không xuất hiện trong đường đi
của truy vấn.

Cụ thể trong `MovieCatalogService` và `ShowtimeQueryService`:

- Ghép điều kiện bằng cách nối `Where` lên `IQueryable`, chỉ `ToListAsync()` ở bước cuối.
- `AsNoTracking()` cho mọi truy vấn chỉ đọc — không cần change tracker cho dữ liệu chỉ để hiển thị.
- `Select` vào DTO (`MovieListItem`, `ShowtimeListItem`) **trước** khi materialize, để SQL chỉ trả
  về đúng các cột cần vẽ ra màn hình.
- Lọc theo ngày: đổi "một ngày ở giờ rạp" thành nửa khoảng UTC `[từ, đến)` **trước** khi truy vấn,
  rồi so sánh `StartTimeUtc` với hai mốc đó. So sánh này chạy được trên index
  `(RoomId, StartTimeUtc)`; nếu cộng offset múi giờ cho từng dòng thì index thành vô dụng.
- Dùng `ToLower()` thay cho `StringComparison`: EF không dịch được overload có `StringComparison`,
  còn `ToLower()` cho cùng kết quả trên SQL Server lẫn provider InMemory dùng trong test.

## Vậy `AsParallel()` dùng ở đâu?

Vẫn còn chỗ dùng đúng, và là chỗ PLINQ thật sự có lãi: **xử lý CPU-bound trên `List<T>` đã nằm
trong RAM**, không có I/O ở giữa. Trong đề tài này đó là:

- `DailyReportWorker` (B4.8): tổng hợp doanh thu trên tập booking đã đọc về.
- Tính điểm gợi ý phim, nếu làm phần mở rộng.

Ngưỡng đáng dùng: tập dữ liệu vài nghìn phần tử trở lên **và** phép tính trên mỗi phần tử không
tầm thường. Dưới ngưỡng đó, chi phí chia partition và gộp kết quả còn lớn hơn phần tiết kiệm được.

## Hệ quả

**Được:**

- Một round-trip cho mỗi màn hình, thay vì đọc cả bảng rồi lọc.
- Truy vấn dùng được index; khi dữ liệu lớn lên, thêm index là đủ, không phải viết lại service.
- Bộ nhớ web process không phình theo số phim/suất chiếu.

**Mất:**

- Logic lọc bị giới hạn trong những gì EF dịch được sang SQL. Điều kiện nào phức tạp quá thì phải
  tách làm hai bước rõ ràng: SQL thu hẹp trước, LINQ-to-Objects xử lý phần còn lại — và bước hai
  phải có comment nói rõ vì sao.
- So sánh chuỗi phụ thuộc collation của SQL Server. Test dùng provider InMemory nên không bắt được
  khác biệt collation; ràng buộc nào thật sự quan trọng thì phải kiểm trên DB thật.

**Luật giữ về sau:** thấy `ToList()`, `AsEnumerable()` hay `AsParallel()` nằm **giữa** chuỗi
`IQueryable` trong PR là dấu hiệu truy vấn đã rơi về client — reviewer chặn lại và hỏi.

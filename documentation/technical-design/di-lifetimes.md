# Vòng đời DI trong CinemaBooking.Smart

> Dev B · Sprint 1 · bổ sung cho [`General.md` §7.6](../../General.md)

Toàn bộ đăng ký nằm trong một chỗ duy nhất:
[`AddCinemaInfrastructure`](../../src/CinemaBooking.Infrastructure/DependencyInjection.cs).
Tài liệu này giải thích **vì sao** mỗi service có vòng đời như hiện tại, để người sau không
"sửa cho gọn" rồi làm hỏng luồng giữ ghế.

## 1. Bảng vòng đời

| Service | Vòng đời | Lý do |
|---|---|---|
| `CinemaDbContext` | Scoped | `DbContext` không thread-safe; mỗi request một unit of work, mỗi request một transaction |
| `ISeatLockManager` → `InMemorySeatLockManager` | **Singleton** | Trạng thái lock nằm trong RAM của process; Scoped thì mỗi request có một bảng lock riêng và không ai thấy ghế của ai |
| `IPaymentService` → `MockPaymentService` | Scoped (qua `AddHttpClient<,>`) | `HttpMessageHandler` được factory pool lại; service chỉ là vỏ mỏng, tạo mới mỗi request là rẻ |
| `IEmailSender` → `SmtpEmailSender` nếu có `Email:Smtp:Host`, ngược lại `LoggingEmailSender` | Scoped | Không giữ trạng thái; để Scoped cho đồng nhất với phần còn lại của request |
| `SeatHub` | Transient (SignalR tự quản) | SignalR tạo một instance cho **mỗi lần gọi hub method** — xem §4 |

## 2. Cái bẫy: Singleton không được inject Scoped

`InMemorySeatLockManager` là Singleton. Nếu ai đó thêm `CinemaDbContext` vào constructor của nó:

```csharp
// ❌ SAI — ném InvalidOperationException ngay khi container validate scope
public InMemorySeatLockManager(CinemaDbContext db) { ... }
```

lỗi sẽ là:

```text
System.InvalidOperationException: Cannot consume scoped service
'CinemaBooking.Infrastructure.Persistence.CinemaDbContext' from singleton
'CinemaBooking.Domain.Abstractions.ISeatLockManager'.
```

Đây là **captive dependency**: một `DbContext` sống theo request bị một object sống theo process
"bắt giữ", và sẽ bị dùng lại xuyên request cho tới khi hỏng (connection đóng, change tracker phình
to, hai luồng cùng gọi `SaveChanges`). ASP.NET Core bật `ValidateScopes` ở Development nên lỗi nổ
sớm; ở Production mà tắt validate thì nó im lặng và hỏng theo cách khó tìm hơn nhiều.

## 3. Cách làm đúng khi Singleton cần DB

Inject `IServiceScopeFactory` và tự mở scope quanh mỗi lần dùng:

```csharp
public sealed class SomeSingleton(IServiceScopeFactory scopeFactory)
{
    public async Task DoWorkAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
        await db.SaveChangesAsync(ct);
    }   // scope dispose ⇒ DbContext dispose
}
```

Quy tắc: **scope phải ngắn hơn thao tác**, mở ngay trước khi dùng và đóng ngay sau đó — đừng cache
`IServiceProvider` của scope vào field.

`BackgroundService` cũng là Singleton, nên mọi hosted service đều phải theo đúng khuôn này:
`SeatLockCleaner` + `ExpiredBookingCleaner` (Web, có `IHubContext`), `EmailOutboxWorker` +
`DailyReportWorker` (Worker).

`SeatLockCleaner` cố ý chạy trong **process Web**: `ISeatLockManager` là Singleton in-memory của
process đó, nếu đặt ở Worker thì nó quét một bảng lock rỗng và không bao giờ nhả được ghế của Web.

## 4. SignalR Hub không phải nơi giữ trạng thái

`SeatHub` được tạo mới cho **mỗi lần gọi** hub method rồi dispose. Field trên hub không sống qua
lời gọi tiếp theo, kể cả với cùng một connection.

```csharp
// ❌ SAI — mất sạch sau khi method trả về
public class SeatHub : Hub
{
    private readonly Dictionary<int, string> _held = new();
}
```

Vì vậy mọi trạng thái ghế phải nằm ở Singleton `ISeatLockManager`, còn hub chỉ là lớp vận chuyển
message. Hệ quả kèm theo: đừng inject `DbContext` vào hub rồi giữ lại — cứ để Scoped resolve theo
từng lời gọi qua constructor là đúng, nhưng không được lưu nó vào static.

## 5. Vì sao lock để trong RAM lại chấp nhận được

Singleton nghĩa là lock chỉ sống trong **một process**. Chạy nhiều instance sau load balancer thì
hai người ở hai instance sẽ cùng chọn được một ghế. Đề tài chấp nhận đánh đổi này vì:

1. Chốt chặn thật nằm ở DB — unique index `(ShowtimeId, SeatId)` trên `BookingSeat` từ chối ghi
   trùng dù lock RAM có sai (`General.md` §7.5).
2. Bài toán triển khai một process; scale-out cần `IDistributedSeatLockManager` trên Redis, và
   đó là lý do `ISeatLockManager` được tách thành interface ngay từ Sprint 1.

## 6. Checklist khi thêm service mới

- Có trạng thái phải chia sẻ toàn process không? → Singleton, và **không** được nhận Scoped.
- Có đụng `DbContext` không? → Scoped, hoặc Singleton + `IServiceScopeFactory`.
- Có gọi HTTP ra ngoài không? → `AddHttpClient<TInterface, TImpl>`, không bao giờ `new HttpClient()`
  (`General.md` §7.3).
- Chạy `dotnet run` ở Development một lần: `ValidateScopes` sẽ bắt captive dependency ngay lúc khởi
  động thay vì lúc demo.

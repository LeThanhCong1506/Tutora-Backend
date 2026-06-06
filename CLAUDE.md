# Tutora Platform Backend — LLM Context Guide

> Tài liệu này dành cho LLM (và developer mới) đọc trước khi làm việc với codebase.
> Cập nhật lần cuối: 2026-04-28.

---

## 1. Kiến trúc tổng quan

```
MV.DomainLayer          → Entities, DTOs, Constants, Helpers, Enums
MV.ApplicationLayer     → ServiceInterfaces + Services (business logic)
MV.InfrastructureLayer  → Repositories, EF Core, external clients
MV.PresentationLayer    → ASP.NET Controllers, Middleware
```

Current dependency flow: `Presentation → Application + Infrastructure`, `Infrastructure → Application + Domain`, `Application → Domain`.
`Application` owns repository contracts, `IUnitOfWork`, and `IAppDbContext`; `Infrastructure` implements those contracts with EF Core.
**Không được** import ngược layer (ví dụ Application không import Presentation).

---

## 2. Quy tắc ngôn ngữ (VN vs EN)

| Vị trí | Ngôn ngữ | Ví dụ |
|---|---|---|
| Tên class / method / property / variable | **English** | `BookingService`, `GetLessonByIdAsync` |
| XML `<summary>` doc trong interface | **English** | `/// <summary>Get lesson by id…</summary>` |
| Inline comment trong service implementation | **Vietnamese OK** | `// Kiểm tra booking có tồn tại không` |
| Message trả về cho người dùng/frontend | **Vietnamese** | `"Không tìm thấy buổi học."` |
| Technical exception/log message (`_logger.Log*`) | **English** | `"Lesson {id} checked in by tutor {tutorId}"` |
| Git commit message | Vietnamese hoặc English, nhất quán trong PR | — |

**Nguyên tắc**: Code phải đọc được bằng tiếng Anh từ đầu đến cuối mà không bị đứt mạch. Message người dùng có thể dùng tiếng Việt. Comment VN dùng để giải thích *tại sao*, không giải thích *cái gì*.

---

## 3. Quy ước đặt tên DTO

### 3.1 Suffix bắt buộc

| Suffix | Dùng khi | Ví dụ |
|---|---|---|
| `*Request` | Input từ client | `CreateBookingRequest`, `CheckInRequest` |
| `*Response` | Output trả về client | `BookingResponse`, `LessonDetailResponse` |
| `*Parameters` | Filter/sort cho paged query | `UserParameters`, `AdminUserFilterParameters` |

### 3.2 Prefix/suffix cho Response chuyên biệt

| Pattern | Ý nghĩa | Ví dụ |
|---|---|---|
| `*DetailResponse` | Full detail (tutor/parent actions, includes all fields) | `LessonDetailResponse` |
| `*SummaryResponse` | Danh sách rút gọn (list view) | `StudentLessonSummaryResponse` |
| `*PreviewResponse` | Public card tìm kiếm, cached | `TutorProfilePreviewResponse` |
| `*FullProfileResponse` | Landing page đầy đủ, cached | `TutorFullProfileResponse` |
| `*ShortResponse` | 3–5 field embed trong entity khác | `TutorProfileShortResponse` |
| `*MiniResponse` | Payload nhỏ nhất, dùng nội bộ giữa service | `LessonMiniResponse` |
| `*PagedResponse` | Tự chứa pagination meta | `TransactionHistoryPagedResponse` |
| `PagedList<T>` | Generic wrapper cho paged list | `PagedList<BookingResponse>` |

### 3.3 Không được làm

- **Không dùng DTO suffix thuần** (`LessonDto`, `UserDto`) — không rõ direction.
- **Không lồng class DTO** trong file Service — extract ra `MV.DomainLayer/DTO/`.
- **Không tạo DTO trùng nghĩa** — trước khi tạo DTO mới, kiểm tra xem đã có DTO tương tự chưa.

---

## 4. Magic strings — cấm tuyệt đối

Mọi string domain (status, type, role, action) phải dùng constant trong `MV.DomainLayer/Constants/`.

### Constant files hiện có

| File | Constants |
|---|---|
| `BookingStatus.cs` | `Pending`, `Confirmed`, `Active`, `Completed`, `Cancelled`, … |
| `LessonStatus.cs` | `Scheduled`, `CheckedIn`, `Completed`, `Cancelled`, `NoShow`, … |
| `PaymentStatus.cs` | `Pending`, `Paid`, `Failed`, `Refunded` |
| `UserRoles.cs` | `Admin`, `Tutor`, `Parent`, `Student` |
| `DiscountType.cs` | `Percent`, `Fixed` |
| `SubscriptionType.cs` | `Free`, `Guided`, `Intensive`, `Elite` |
| `EarningsPeriod.cs` | `Week`, `Month`, `Year` |
| `FeedbackType.cs` | `PostLesson`, `ParentToTutor` |
| `NoShowActionTypes.cs` | `FreeSession`, `Makeup`, `ChangeTutor` |
| `DisputeTypes.cs` | … |
| `ZaloChatbotState.cs` | `Idle`, `AskSubject`, `AskGrade`, `AskArea` |
| `ZaloChatbotIntent.cs` | `FindTutor`, `ViewCalendar`, `Contact` |
| `ZaloOAEventType.cs` | `Follow`, `Unfollow`, `UserSendText` |
| `Currency.cs` | `Vnd` |

**Quy tắc**: Khi cần thêm domain string mới → tạo/mở rộng file trong `Constants/`, không hardcode literal.

---

## 5. DateTime — UTC storage, Vietnam display

```csharp
// ✅ Đúng khi lưu DB / so sánh kỹ thuật
var now = DateTime.UtcNow;

// ✅ Đúng khi hiển thị hoặc tính lịch theo giờ Việt Nam
var displayTime = VietnamTimeHelper.ToVietnamTime(utcTime);

// ❌ Sai
var now = DateTime.Now;            // phụ thuộc server timezone
var now = DateTime.UtcNow.AddHours(7);  // verbose, dễ sai
```

Helper ở `MV.DomainLayer/Helpers/VietnamTimeHelper.cs`.

---

## 6. Service splitting (Partial class pattern)

Các service lớn (> ~400 dòng) được chia thành partial class files:

```
TutorVerificationService.cs           ← constructor, DI fields, core helpers
TutorVerificationService.Profile.cs   ← GetTutorProfilePreviewAsync, GetTutorFullProfileAsync
TutorVerificationService.Progress.cs  ← GetVerificationProgressAsync, UpdateTutorStatusToPendingAsync
```

**Quy tắc**:
- Mỗi partial file có `using` directives riêng — chỉ import những gì file đó cần.
- Chia theo **feature group**, không chia theo alphabetical.
- Core file giữ constructor + injected fields + private helpers dùng chung.

---

## 7. Interface docs — bắt buộc

Mọi method trong `IServiceInterfaces/` phải có `/// <summary>`. Không dùng comment `//` thuần.

```csharp
// ❌ Không dùng
// Get lesson by id
Task<LessonResponse?> GetLessonByIdAsync(int lessonId, string userId, bool isParent);

// ✅ Dùng
/// <summary>
/// Single lesson by id — ownership-checked against the calling user.
/// </summary>
Task<LessonResponse?> GetLessonByIdAsync(int lessonId, string userId, bool isParent);
```

---

## 8. Caching conventions

| Service | Cache duration | Key pattern |
|---|---|---|
| `GetTutorProfilePreviewAsync` | 15 phút | `tutor:preview:{tutorId}` |
| `GetTutorFullProfileAsync` | 20 phút | `tutor:full:{tutorId}` |

Cache dùng `IDistributedCache` (Redis). Invalidate khi tutor cập nhật profile.

---

## 9. Layer violations cần tránh

- **Application service** không được import namespace `MV.ApplicationLayer.Services` trong interface file.
- **Interface file** (`IXxx.cs`) không được chứa inline class definition — extract ra `DomainLayer/DTO/`.
- **Controller** không được gọi Repository trực tiếp — chỉ gọi qua Service interface.

---

## 10. TODO policy

- TODO comment trong code → phải có ticket/issue đính kèm hoặc resolve trong PR hiện tại.
- Không để `// TODO:` trôi nổi không rõ owner.
- Kiểm tra bằng: `grep -rn "TODO" --include="*.cs" MV.ApplicationLayer/ MV.DomainLayer/`

---

## 11. Files đặc biệt cần biết

| File | Mục đích |
|---|---|
| `MV.DomainLayer/Helpers/VietnamTimeHelper.cs` | UTC+7 DateTime conversion helper |
| `MV.DomainLayer/Constants/` | Toàn bộ domain string constants |
| `MV.DomainLayer/DTO/ResponseModel/` | Toàn bộ response DTO |
| `MV.DomainLayer/DTO/RequestModel/` | Toàn bộ request DTO |
| `MV.ApplicationLayer/Services/LessonService.M3.cs` | Calendar, attendance, no-show logic (666 dòng — candidate split) |
| `MV.InfrastructureLayer/Repositories/TutorSearchRepository.cs` | Full-text tutor search với subscription tier filter |

---

## 12. Luồng payment (hai pha)

```
Booking created
  → Deposit phase  (30% hoặc cố định)  → ZaloPay hoặc Wallet
  → Remaining phase (70%)              → Sau khi booking hoàn thành
```

`PaymentStatusResponse` track cả hai pha qua `IsDepositPaid` + `IsRemainingPaid`.

---

## 13. Zalo integration

| Service | Trách nhiệm |
|---|---|
| `IZaloAuthService` | SSO login bằng Zalo token → issue Tutora JWT |
| `IZaloOAService` | Gửi template message, xử lý OA webhook, reply tin nhắn |
| `IZaloChatbotService` | State machine chatbot tìm gia sư qua Zalo OA |

State machine của chatbot: `IDLE → ASK_SUBJECT → ASK_GRADE → ASK_AREA → [search results]`.
Intent trigger: `#tim_gia_su`, `#xem_lich`, `#lien_he` (constants trong `ZaloChatbotIntent.cs`).

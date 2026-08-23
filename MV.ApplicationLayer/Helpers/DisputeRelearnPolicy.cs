using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Link 3: khi hai bên hoà giải một tranh chấp và đồng ý học lại (CloseDisputeOutcomes.Reschedule),
/// buổi học lại là 1 ClassSession MỚI (Isdisputerelearn=true), không dùng lại đúng row cũ — giữ
/// nguyên toàn bộ dữ liệu buổi gốc (check-in, điểm danh, ghi hình) để tra cứu, khác với hành vi cũ
/// (reset-tại-chỗ + xoá dấu vết). Tách riêng khỏi DisputeService vì service đó dùng FromSqlRaw để
/// lock row (đặc thù Postgres) — không test được với EF InMemory provider.
/// </summary>
public static class DisputeRelearnPolicy
{
    /// <summary>Số buổi TỐI ĐA được phép có trong 1 chuỗi (buổi gốc + mọi buổi bù/phụ do gián đoạn
    /// + mọi buổi học lại do hoà giải, bất kể loại nào) — hễ chuỗi đã đủ 3 buổi thì không được tạo
    /// thêm buổi nào nữa (kể cả do gián đoạn hay do hoà giải); nếu buổi thứ 3 lại bị khiếu nại,
    /// bắt buộc xử lý bằng hoàn tiền qua "Ra quyết định". Đếm theo TỔNG số buổi trong chuỗi (không
    /// riêng buổi Isdisputerelearn) vì buổi bù do gián đoạn (Iscontinuation) cũng chiếm 1 vị trí
    /// trong chuỗi y hệt buổi học lại do hoà giải.</summary>
    public const int MaxRelearnSessionsPerChain = 3;

    /// <summary>Ném ArgumentException nếu outcome=Reschedule mà thiếu giờ học lại hoặc giờ đó không
    /// ở tương lai. Không làm gì nếu outcome khác Reschedule.</summary>
    public static void ValidateRelearnRequest(CloseDisputeRequest request, DateTime now)
    {
        if (request.ClassSessionOutcome != CloseDisputeOutcomes.Reschedule)
            return;

        if (!request.RelearnScheduledStart.HasValue)
            throw new ArgumentException("Cần chọn giờ học lại khi hai bên đồng ý học lại buổi này.");

        if (request.RelearnScheduledStart.Value <= now)
            throw new ArgumentException("Giờ học lại phải ở tương lai.");
    }

    /// <summary>Đếm TỔNG số buổi (buổi gốc + mọi buổi bù/phụ/học lại, không phân biệt loại) đã có
    /// trong cùng chuỗi chứa classSessionId — dùng để chặn tạo thêm buổi (do gián đoạn hoặc do hoà
    /// giải) khi chuỗi đã đạt MaxRelearnSessionsPerChain buổi. Đi chuỗi bằng
    /// ClassSessionRecordingChainHelper (cùng nguồn với trang xem lại video) để không lệch định
    /// nghĩa "cùng 1 chuỗi" giữa 2 nơi. Cố ý đếm CẢ buổi Iscontinuation lẫn Isdisputerelearn — nếu
    /// chỉ đếm riêng Isdisputerelearn thì 1 buổi bù do gián đoạn xen giữa chuỗi sẽ không bị tính,
    /// khiến chuỗi vượt quá 3 buổi thực tế mà vẫn chưa chạm cap.</summary>
    public static async Task<int> CountSessionsInChainAsync(IAppDbContext context, int classSessionId)
    {
        var chain = await ClassSessionRecordingChainHelper.GetChainAsync(context, classSessionId);
        return chain?.Count ?? 0;
    }

    /// <summary>
    /// Dựng row ClassSession mới cho buổi học lại — thuần tính toán, không đụng DB. Giữ nguyên
    /// Bookingid/Tutorid/Studentid và thời lượng của buổi gốc; Lessonprice=0 vì không phải đơn vị
    /// thanh toán riêng (tiền đi theo buổi gốc trong booking, không quyết toán buổi này riêng).
    /// </summary>
    public static ClassSession BuildRelearnSession(ClassSession original, DateTime relearnScheduledStart, DateTime now)
    {
        var duration = original.Scheduledend - original.Scheduledstart;

        return new ClassSession
        {
            Bookingid = original.Bookingid,
            Tutorid = original.Tutorid,
            Studentid = original.Studentid,
            Isdisputerelearn = true,
            Originalsessionid = original.Classsessionid,
            Lessonprice = 0,
            Status = ClassSessionStatus.Scheduled,
            Scheduledstart = relearnScheduledStart,
            Scheduledend = relearnScheduledStart.Add(duration),
            Createdat = now
        };
    }
}

using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.Helpers;
using StackExchange.Redis;
using System.Text.Json;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Lưu bản đề xuất chỉnh sửa hồ sơ Tutor (đang Active) vào Redis, chờ Admin duyệt.
    /// Tutorprofile thật (Postgres) chỉ được ghi khi Admin approve — xem TutorService.cs
    /// (UpdateTutorBasicInfoAsync/UpdateTutorIntroductionAsync/UpdateTutorPricingAsync,
    /// TutorService.Media.cs UpdateTutorVideoAsync) và TutorService.Admin.cs (ReviewProfileUpdateRequestAsync).
    /// </summary>
    public class TutorProfileUpdateStagingService : ITutorProfileUpdateStagingService
    {
        private const string KeyPrefix = "tutor:profile_update:";
        private const string PendingIdsKey = "tutor:profile_update:pending_ids";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<TutorProfileUpdateStagingService> _logger;

        public TutorProfileUpdateStagingService(IConnectionMultiplexer redis, ILogger<TutorProfileUpdateStagingService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        private static string RequestKey(string tutorId) => $"{KeyPrefix}{tutorId}";

        public async Task<PendingTutorProfileUpdate?> GetPendingUpdateAsync(string tutorId)
        {
            try
            {
                var db = _redis.GetDatabase();
                var json = await db.StringGetAsync(RequestKey(tutorId));
                if (json.IsNullOrEmpty) return null;

                return JsonSerializer.Deserialize<PendingTutorProfileUpdate>(json!);
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis lỗi khi đọc pending update của tutor {TutorId}", tutorId);
                throw new ArgumentException("Không thể tải bản cập nhật đang chờ duyệt lúc này, vui lòng thử lại sau.");
            }
        }

        public async Task<(PendingTutorProfileUpdate? Data, string? RawJson)> GetPendingUpdateWithRawAsync(string tutorId)
        {
            try
            {
                var db = _redis.GetDatabase();
                var json = await db.StringGetAsync(RequestKey(tutorId));
                if (json.IsNullOrEmpty) return (null, null);

                var rawJson = json.ToString();
                return (JsonSerializer.Deserialize<PendingTutorProfileUpdate>(rawJson), rawJson);
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis lỗi khi đọc pending update của tutor {TutorId}", tutorId);
                throw new ArgumentException("Không thể tải bản cập nhật đang chờ duyệt lúc này, vui lòng thử lại sau.");
            }
        }

        public async Task UpsertPendingUpdateAsync(string tutorId, Action<PendingTutorProfileUpdate> applyChanges)
        {
            try
            {
                var db = _redis.GetDatabase();
                var key = RequestKey(tutorId);

                var existingJson = await db.StringGetAsync(key);
                var pending = existingJson.IsNullOrEmpty
                    ? new PendingTutorProfileUpdate { TutorId = tutorId }
                    : JsonSerializer.Deserialize<PendingTutorProfileUpdate>(existingJson!)!;

                applyChanges(pending);
                pending.Status = TutorProfileUpdateStatus.Pending;
                pending.SubmittedAt = TimeZoneHelper.UtcNow;

                var newJson = JsonSerializer.Serialize(pending);
                await db.StringSetAsync(key, newJson);
                await db.SetAddAsync(PendingIdsKey, tutorId);

                // Bản sao dự phòng thủ công: nếu Redis mất dữ liệu, dev tra log để lấy lại nội
                // dung tutor đã nộp lần cuối. Log không nằm trên bất kỳ read-path nào của ứng
                // dụng nên không ảnh hưởng luồng chính.
                _logger.LogInformation(
                    "Tutor {TutorId} submitted a profile update pending admin review. Payload: {Payload}",
                    tutorId, newJson);
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis lỗi khi lưu pending update của tutor {TutorId}", tutorId);
                throw new ArgumentException("Không thể lưu thay đổi lúc này, vui lòng thử lại sau.");
            }
        }

        public async Task ClearPendingUpdateAsync(string tutorId)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(RequestKey(tutorId));
                await db.SetRemoveAsync(PendingIdsKey, tutorId);
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis lỗi khi xoá pending update của tutor {TutorId}", tutorId);
                throw new ArgumentException("Không thể hoàn tất thao tác lúc này, vui lòng thử lại sau.");
            }
        }

        public async Task<bool> ClearPendingUpdateIfUnchangedAsync(string tutorId, string expectedRawJson)
        {
            try
            {
                var db = _redis.GetDatabase();
                var key = RequestKey(tutorId);

                // Compare-and-delete nguyên tử: nếu Tutor vừa upsert thêm thay đổi mới sau khi
                // Admin đã đọc (expectedRawJson là bản admin đọc lúc đó), nội dung key hiện tại sẽ
                // khác đi → điều kiện không khớp → transaction KHÔNG commit → key được GIỮ LẠI,
                // không bị xoá theo, tránh mất thay đổi mới nhất của tutor (race condition).
                var transaction = db.CreateTransaction();
                transaction.AddCondition(Condition.StringEqual(key, expectedRawJson));
                transaction.KeyDeleteAsync(key);
                transaction.SetRemoveAsync(PendingIdsKey, tutorId);

                var committed = await transaction.ExecuteAsync();
                if (!committed)
                {
                    _logger.LogWarning(
                        "Không xoá được pending update của tutor {TutorId} vì đã có thay đổi mới trong lúc xử lý — bản mới được giữ lại cho lần duyệt sau.",
                        tutorId);
                }

                return committed;
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis lỗi khi xoá (có điều kiện) pending update của tutor {TutorId}", tutorId);
                throw new ArgumentException("Không thể hoàn tất thao tác lúc này, vui lòng thử lại sau.");
            }
        }

        public async Task<List<string>> GetAllPendingTutorIdsAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var members = await db.SetMembersAsync(PendingIdsKey);
                return members.Select(m => m.ToString()).ToList();
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "Redis lỗi khi lấy danh sách pending update requests");
                throw new ArgumentException("Không thể tải danh sách yêu cầu cập nhật lúc này, vui lòng thử lại sau.");
            }
        }
    }
}

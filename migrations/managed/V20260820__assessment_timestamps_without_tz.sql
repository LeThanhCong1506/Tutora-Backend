-- Sửa lệch giờ ở bảng assessment: started_at/submitted_at bị cộng dồn UTC+7 mỗi vòng
-- ghi–đọc, tới mức "nộp bài trước khi bắt đầu" và duration_seconds luôn = 0.

BEGIN;

-- USING ... AT TIME ZONE 'UTC': lấy đúng phần thời điểm đang lưu, không dịch thêm lần nữa.
ALTER TABLE public.assessment_attempts
    ALTER COLUMN started_at   TYPE timestamp USING started_at   AT TIME ZONE 'UTC',
    ALTER COLUMN submitted_at TYPE timestamp USING submitted_at AT TIME ZONE 'UTC',
    ALTER COLUMN expires_at   TYPE timestamp USING expires_at   AT TIME ZONE 'UTC',
    ALTER COLUMN analyzed_at  TYPE timestamp USING analyzed_at  AT TIME ZONE 'UTC';

-- now() trả timestamptz -> ép về timestamp cho khớp kiểu cột mới.
ALTER TABLE public.assessment_attempts
    ALTER COLUMN started_at SET DEFAULT (now() AT TIME ZONE 'UTC');

-- Gỡ phần lệch đã tích luỹ trong dữ liệu cũ. Mốc chuẩn là analyzed_at (+7h, tức giờ VN
-- ghi vào cột như thể UTC) — đó là quy ước đang dùng của hệ, giữ nguyên.
UPDATE public.assessment_attempts
SET started_at   = started_at   - interval '14 hours',
    submitted_at = submitted_at - interval '7 hours'
WHERE submitted_at IS NOT NULL
  AND submitted_at < started_at;

-- Tính lại từ hai mốc đã chỉnh.
UPDATE public.assessment_attempts
SET duration_seconds = GREATEST(0, EXTRACT(EPOCH FROM (submitted_at - started_at))::int)
WHERE submitted_at IS NOT NULL
  AND (duration_seconds IS NULL OR duration_seconds = 0);

COMMENT ON COLUMN public.assessment_attempts.started_at IS
    'timestamp KHÔNG timezone — khớp EnableLegacyTimestampBehavior + convention Kind=Utc
     của AgoraDbContext. Đổi sang timestamptz sẽ làm giá trị bị dịch thêm mỗi vòng ghi–đọc.';

COMMIT;

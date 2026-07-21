-- Dọn dữ liệu theo dõi hành vi cũ để bảng không phình vô hạn.
--
-- Chính sách: mẫu điểm chi tiết (alert_reason IS NULL) chỉ hữu ích để vẽ biểu đồ ngay sau buổi,
-- sau 90 ngày thì xoá. Các dòng CẢNH BÁO (alert_reason IS NOT NULL) được giữ lâu hơn (1 năm)
-- vì đó là bằng chứng/lịch sử sự cố, số lượng ít.
--
-- Chạy định kỳ bằng pg_cron nếu có; nếu không, gọi thủ công hàm này từ job bên ngoài.

CREATE OR REPLACE FUNCTION prune_session_engagement_samples()
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count integer;
BEGIN
    -- Mẫu điểm thường: giữ 90 ngày
    DELETE FROM session_engagement_samples
    WHERE alert_reason IS NULL
      AND sampled_at < NOW() - INTERVAL '90 days';
    GET DIAGNOSTICS deleted_count = ROW_COUNT;

    -- Cảnh báo: giữ 1 năm
    DELETE FROM session_engagement_samples
    WHERE alert_reason IS NOT NULL
      AND sampled_at < NOW() - INTERVAL '365 days';

    RETURN deleted_count;
END;
$$;

-- Lên lịch chạy hằng ngày lúc 3h sáng nếu instance có pg_cron. Không có extension thì bỏ qua
-- (DO block nuốt lỗi) — lúc đó gọi prune_session_engagement_samples() từ job bên ngoài.
DO $$
BEGIN
    PERFORM cron.schedule(
        'prune-session-engagement-samples',
        '0 3 * * *',
        'SELECT prune_session_engagement_samples();'
    );
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'pg_cron không khả dụng — hãy gọi prune_session_engagement_samples() định kỳ từ bên ngoài.';
END;
$$;

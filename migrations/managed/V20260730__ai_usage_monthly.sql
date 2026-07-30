-- V20260730 — Thống kê lượt dùng AI theo tháng + chuẩn hoá dữ liệu credit.
--
-- ai_credit_transactions chỉ ghi GRANT/PURCHASE, không ghi SPEND (xem V20260726)
-- nên không biết người dùng đã hỏi bao nhiêu lượt. Bảng ai_usage_monthly gộp theo
-- (tài khoản, tháng), UPSERT tăng dần — không ghi từng lượt.
--
-- Toàn bộ idempotent, chạy lại nhiều lần vẫn ra cùng kết quả.

BEGIN;

-- 1. Bảng tổng hợp theo tháng
CREATE TABLE IF NOT EXISTS public.ai_usage_monthly (
    user_id    varchar(50) NOT NULL,
    period     date        NOT NULL,   -- ngày đầu tháng (UTC)
    used_count integer     NOT NULL DEFAULT 0,
    updated_at timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    CONSTRAINT pk_ai_usage_monthly PRIMARY KEY (user_id, period),
    CONSTRAINT fk_ai_usage_monthly_user
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_ai_usage_monthly_period
    ON public.ai_usage_monthly (period DESC, used_count DESC);

-- 2. Backfill từ chat_histories — mỗi tin nhắn role='assistant' = 1 lượt trừ credit
INSERT INTO public.ai_usage_monthly (user_id, period, used_count)
SELECT
    s.user_id,
    date_trunc('month', h.created_at)::date,
    COUNT(*)
FROM public.chat_histories h
JOIN public.chat_sessions  s ON s.session_id = h.session_id
WHERE h.role = 'assistant'
GROUP BY s.user_id, date_trunc('month', h.created_at)::date
ON CONFLICT (user_id, period)
DO UPDATE SET used_count = EXCLUDED.used_count,
              updated_at = (now() AT TIME ZONE 'utc');

-- 3. Dọn cột thừa của bản nháp trước (đã thay bằng bảng ở trên)
DROP INDEX IF EXISTS public.idx_users_ai_credits_used_total;

ALTER TABLE public.users
    DROP COLUMN IF EXISTS ai_credits_used_total;

-- 4. Tặng gói Free cho mọi vai trò + chuẩn hoá mốc thời gian.
--    Script cũ chỉ tặng cho Student. Mốc cấp = ngày AI ra mắt, vì người đăng ký
--    trước đó chỉ thực sự dùng được từ lúc tính năng chạy.
DO $$
DECLARE
    free_amount    integer;
    granted_count  integer := 0;
    fixed_count    integer := 0;
    ai_launch_date constant timestamp := '2026-07-01 00:00:00';
BEGIN
    UPDATE public.ai_credit_transactions t
    SET created_at = ai_launch_date
    WHERE t.source       = 'grant'
      AND t.reference_id = 'free:' || t.user_id
      AND t.created_at  <> ai_launch_date;

    GET DIAGNOSTICS fixed_count = ROW_COUNT;
    RAISE NOTICE 'Da chuan hoa moc thoi gian cho % dong tang goi Free.', fixed_count;

    SELECT credit_amount INTO free_amount
    FROM public.ai_credit_packages
    WHERE LOWER(code) = 'free' AND is_active = TRUE
    LIMIT 1;

    IF free_amount IS NULL OR free_amount <= 0 THEN
        RAISE NOTICE 'Khong tim thay goi free active. Bo qua buoc tang.';
        RETURN;
    END IF;

    WITH eligible AS (
        SELECT u.user_id, u.ai_credits_balance, u.created_at
        FROM public.users u
        WHERE NOT EXISTS (
            SELECT 1 FROM public.ai_credit_transactions t
            WHERE t.user_id      = u.user_id
              AND t.source       = 'grant'
              AND t.reference_id = 'free:' || u.user_id
        )
    ),
    ins AS (
        INSERT INTO public.ai_credit_transactions
            (user_id, amount, balance_after, source, reference_id, description, created_at)
        SELECT
            e.user_id,
            free_amount,
            e.ai_credits_balance + free_amount,
            'grant',
            'free:' || e.user_id,
            'Tang goi Free (backfill cho moi vai tro).',
            GREATEST(COALESCE(e.created_at, ai_launch_date), ai_launch_date)
        FROM eligible e
        RETURNING user_id
    )
    UPDATE public.users u
    SET ai_credits_balance = u.ai_credits_balance + free_amount
    FROM ins
    WHERE u.user_id = ins.user_id;

    GET DIAGNOSTICS granted_count = ROW_COUNT;
    RAISE NOTICE 'Da tang % luot Free cho % tai khoan.', free_amount, granted_count;
END $$;

COMMIT;

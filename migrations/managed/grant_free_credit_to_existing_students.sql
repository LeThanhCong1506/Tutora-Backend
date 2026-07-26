-- =====================================================
-- Backfill: tặng credit gói Free cho MỌI tài khoản role Student đang có,
-- =====================================================
BEGIN;

DO $$
DECLARE
    free_amount integer;
    granted_count integer := 0;
BEGIN
    -- Số lượt của gói Free (đang active). Nếu chưa có gói free -> bỏ qua an toàn.
    SELECT credit_amount INTO free_amount
    FROM public.ai_credit_packages
    WHERE LOWER(code) = 'free' AND is_active = TRUE
    LIMIT 1;

    IF free_amount IS NULL OR free_amount <= 0 THEN
        RAISE NOTICE 'Khong tim thay goi free active (hoac credit_amount <= 0). Bo qua.';
        RETURN;
    END IF;

    -- Danh sách student đủ điều kiện: role Student, chưa nhận free grant.
    WITH eligible AS (
        SELECT u.user_id, u.ai_credits_balance
        FROM public.users u
        WHERE u.primary_role = 'Student'
          AND NOT EXISTS (
              SELECT 1 FROM public.ai_credit_transactions t
              WHERE t.user_id = u.user_id
                AND t.source = 'grant'
                AND t.reference_id = 'free:' || u.user_id
          )
    ),
    -- 1. Ghi ledger cho từng người (balance_after = số dư hiện tại + free_amount).
    ins AS (
        INSERT INTO public.ai_credit_transactions
            (user_id, amount, balance_after, source, reference_id, description, created_at)
        SELECT
            e.user_id,
            free_amount,
            e.ai_credits_balance + free_amount,
            'grant',
            'free:' || e.user_id,
            'Tang goi Free (backfill cho tai khoan da ton tai).',
            CURRENT_TIMESTAMP
        FROM eligible e
        RETURNING user_id
    )
    -- 2. Cộng cache balance trên users.
    UPDATE public.users u
    SET ai_credits_balance = u.ai_credits_balance + free_amount
    FROM ins
    WHERE u.user_id = ins.user_id;

    GET DIAGNOSTICS granted_count = ROW_COUNT;
    RAISE NOTICE 'Da tang % luot Free cho % tai khoan Student.', free_amount, granted_count;
END $$;

COMMIT;

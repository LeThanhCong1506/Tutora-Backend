-- Credit AI có HẠN DÙNG, và số tháng do admin chỉnh — không hardcode.
--
-- Trước đây balance chỉ là một con số trên users.ai_credits_balance, không biết lượt nào
-- cấp lúc nào nên không thể hết hạn. Nay mỗi lần cấp là một LÔ (batch) có hạn riêng:
-- tiêu thì trừ lô sắp hết hạn trước (FIFO theo expires_at), lô quá hạn thì bỏ qua.
--
-- Áp cho CẢ BA nguồn: tặng lúc xác thực SĐT, thưởng booking, và mua gói.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ai_credit_batches (
    id           uuid         NOT NULL DEFAULT gen_random_uuid(),
    user_id      varchar(50)  NOT NULL,

    -- free_signup | booking_bonus | purchase
    source       varchar(30)  NOT NULL,
    -- Khoá chống cấp trùng: 'free:<userId>', 'booking:<id>', 'purchase:<orderCode>'.
    reference_id varchar(120) NULL,

    granted      integer      NOT NULL,
    -- Đã tiêu bao nhiêu của lô này. remaining = granted - consumed.
    consumed     integer      NOT NULL DEFAULT 0,

    granted_at   timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'),
    -- NULL = không hết hạn (dự phòng, hiện mọi lô đều có hạn).
    expires_at   timestamp    NULL,

    CONSTRAINT ai_credit_batches_pkey PRIMARY KEY (id),
    CONSTRAINT ai_credit_batches_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (user_id) ON DELETE CASCADE,
    CONSTRAINT ai_credit_batches_granted_check  CHECK (granted > 0),
    CONSTRAINT ai_credit_batches_consumed_check CHECK (consumed >= 0 AND consumed <= granted)
);

-- Đường nóng: lấy các lô CÒN HẠN, CÒN DƯ của 1 user, sắp hết hạn trước.
CREATE INDEX IF NOT EXISTS idx_ai_credit_batches_user_expiry
    ON public.ai_credit_batches (user_id, expires_at)
    WHERE consumed < granted;

-- Chống cấp trùng cùng một nguồn (đăng ký lại, webhook gọi 2 lần...).
CREATE UNIQUE INDEX IF NOT EXISTS uq_ai_credit_batches_reference
    ON public.ai_credit_batches (user_id, source, reference_id)
    WHERE reference_id IS NOT NULL;

COMMENT ON TABLE public.ai_credit_batches IS
    'Mỗi lần cấp credit = 1 lô có hạn riêng. Tiêu theo FIFO expires_at (sắp hết hạn tiêu
     trước) để học sinh không mất oan lượt còn hạn dài.';

-- Số tháng hết hạn — admin chỉnh trong CMS, KHÔNG hardcode trong code.
INSERT INTO public.system_configs (config_key, config_value, description, updated_at)
VALUES ('ai_credit_expiry_months', '3',
        'So THANG credit AI het han ke tu ngay cap. Ap cho ca 3 nguon: tang khi xac thuc SDT, thuong booking, mua goi.',
        CURRENT_TIMESTAMP)
ON CONFLICT (config_key) DO NOTHING;

-- Số credit tặng khi xác thực SĐT thành công (trước đây lấy từ package Free).
INSERT INTO public.system_configs (config_key, config_value, description, updated_at)
VALUES ('ai_credit_free_on_signup', '10',
        'So luot AI tang khi tai khoan moi XAC THUC SO DIEN THOAI thanh cong.',
        CURRENT_TIMESTAMP)
ON CONFLICT (config_key) DO NOTHING;

COMMIT;

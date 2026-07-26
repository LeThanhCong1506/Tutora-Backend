-- =====================================================
-- V20260726 — AI Credit (Homework Helper): setup schema hoàn chỉnh.
--
-- Thiết kế:
--   * Credit gắn với TÀI KHOẢN ĐĂNG NHẬP (users.user_id) — app độc lập, ai login
--     (Student/Tutor/Parent) đều mua & dùng credit trên chính acc đó.
--   * ai_credit_transactions là LEDGER — chỉ ghi GRANT/PURCHASE (tặng/mua, audit tiền thật).
--     KHÔNG ghi cho mỗi lần SPEND (hỏi bài) — tần suất cao, không cần audit từng dòng
--     (xem AiCreditService.SpendAsync: chỉ UPDATE users.ai_credits_balance).
-- =====================================================
BEGIN;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. ai_credit_transactions — đảm bảo key theo user_id (idempotent nếu đã đúng).
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'ai_credit_transactions' AND column_name = 'user_id') THEN
        ALTER TABLE public.ai_credit_transactions ADD COLUMN user_id varchar(50) NOT NULL;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ai_credit_transactions_userid_fkey') THEN
        ALTER TABLE public.ai_credit_transactions
            ADD CONSTRAINT ai_credit_transactions_userid_fkey FOREIGN KEY (user_id)
                REFERENCES public.users(user_id) ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_ai_credit_transactions_user_createdat
    ON public.ai_credit_transactions USING btree (user_id, created_at DESC);

COMMENT ON COLUMN public.ai_credit_transactions.user_id
    IS 'Chu the nhan/tieu credit — users.user_id (tai khoan dang nhap, moi role). Bang nay CHI ghi grant/purchase, khong ghi spend.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. AI credit packages catalog (Free / Plus / Pro) — admin CRUD, icon upload.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE SEQUENCE IF NOT EXISTS public.ai_credit_packages_id_seq
    INCREMENT BY 1 MINVALUE 1 MAXVALUE 2147483647 START 1 CACHE 1 NO CYCLE;

CREATE TABLE IF NOT EXISTS public.ai_credit_packages (
    package_id      integer NOT NULL DEFAULT nextval('ai_credit_packages_id_seq'::regclass),
    code            varchar(30)  NOT NULL,   -- stable identifier: 'free' | 'plus' | 'pro'
    name            varchar(100) NOT NULL,   -- display name
    credit_amount   integer      NOT NULL,   -- admin-configurable number of AI uses
    price           numeric(12,2) NOT NULL DEFAULT 0,  -- Free = 0
    currency        varchar(10)  NOT NULL DEFAULT 'VND',
    is_purchasable  boolean      NOT NULL DEFAULT true, -- Free = false (auto-grant only)
    is_active       boolean      NOT NULL DEFAULT true,
    sort_order      integer      NOT NULL DEFAULT 0,
    description     text NULL,
    icon_url        varchar(1000) NULL,      -- admin upload (Cloudinary); FE dùng icon mặc định nếu null
    created_at      timestamp DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at      timestamp DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ai_credit_packages_pkey PRIMARY KEY (package_id),
    CONSTRAINT chk_ai_credit_packages_amount_nonneg CHECK (credit_amount >= 0),
    CONSTRAINT chk_ai_credit_packages_price_nonneg CHECK (price >= 0)
);

ALTER TABLE public.ai_credit_packages ADD COLUMN IF NOT EXISTS icon_url varchar(1000) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_ai_credit_packages_code
    ON public.ai_credit_packages (LOWER(code));

COMMENT ON TABLE public.ai_credit_packages IS 'Cac goi AI credit (Homework Helper): Free/Plus/Pro. So luot, gia, icon do admin cau hinh.';

-- Seed default packages (idempotent by code).
INSERT INTO public.ai_credit_packages (code, name, credit_amount, price, is_purchasable, is_active, sort_order, description)
VALUES
    ('free', 'Free', 10, 0,       false, true, 0, 'Goi mien phi — tang 10 luot khi tao tai khoan.'),
    ('plus', 'Plus', 100, 49000,  true,  true, 1, 'Goi Plus — admin chinh so luot.'),
    ('pro',  'Pro',  300, 129000, true,  true, 2, 'Goi Pro — admin chinh so luot.')
ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. payment_transactions: cột liên kết gói + tài khoản nhận credit khi mua.
--    Theo pattern polymorphic sẵn có (booking_id/withdrawal_id là mỗi loại 1 cột nullable).
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE public.payment_transactions ADD COLUMN IF NOT EXISTS ai_credit_package_id integer NULL;
ALTER TABLE public.payment_transactions ADD COLUMN IF NOT EXISTS ai_credit_user_id varchar(50) NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'payment_transactions_ai_credit_package_id_fkey') THEN
        ALTER TABLE public.payment_transactions
            ADD CONSTRAINT payment_transactions_ai_credit_package_id_fkey
                FOREIGN KEY (ai_credit_package_id)
                REFERENCES public.ai_credit_packages(package_id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'payment_transactions_ai_credit_user_id_fkey') THEN
        ALTER TABLE public.payment_transactions
            ADD CONSTRAINT payment_transactions_ai_credit_user_id_fkey FOREIGN KEY (ai_credit_user_id)
                REFERENCES public.users(user_id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_payment_transactions_ai_credit_package_id
    ON public.payment_transactions(ai_credit_package_id);
CREATE INDEX IF NOT EXISTS idx_payment_transactions_ai_credit_user_id
    ON public.payment_transactions(ai_credit_user_id);

COMMENT ON COLUMN public.payment_transactions.ai_credit_package_id
    IS 'Goi AI credit duoc mua (purpose = AiCreditPurchase); NULL cho cac loai giao dich khac.';
COMMENT ON COLUMN public.payment_transactions.ai_credit_user_id
    IS 'Tai khoan nhan credit khi mua goi; user_id la nguoi tra tien (thuong trung).';

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. payment_requests: cho phép phục vụ mua AI credit (không thuộc booking).
--    booking_id vốn NOT NULL/phase chỉ deposit-remaining — nới lỏng để dùng chung.
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE public.payment_requests ALTER COLUMN booking_id DROP NOT NULL;

ALTER TABLE public.payment_requests DROP CONSTRAINT IF EXISTS ck_payment_requests_phase;
ALTER TABLE public.payment_requests
    ADD CONSTRAINT ck_payment_requests_phase
        CHECK (phase IN ('deposit', 'remaining', 'legacy_unknown', 'ai_credit'));

ALTER TABLE public.payment_requests DROP CONSTRAINT IF EXISTS ck_payment_requests_booking_scope;
ALTER TABLE public.payment_requests
    ADD CONSTRAINT ck_payment_requests_booking_scope CHECK (
        (phase IN ('deposit', 'remaining', 'legacy_unknown') AND booking_id IS NOT NULL)
        OR (phase = 'ai_credit' AND booking_id IS NULL)
    );

ALTER TABLE public.payment_requests ADD COLUMN IF NOT EXISTS ai_credit_package_id integer NULL;
ALTER TABLE public.payment_requests ADD COLUMN IF NOT EXISTS ai_credit_user_id varchar(50) NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'payment_requests_ai_credit_package_id_fkey') THEN
        ALTER TABLE public.payment_requests
            ADD CONSTRAINT payment_requests_ai_credit_package_id_fkey
                FOREIGN KEY (ai_credit_package_id)
                REFERENCES public.ai_credit_packages(package_id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'payment_requests_ai_credit_user_id_fkey') THEN
        ALTER TABLE public.payment_requests
            ADD CONSTRAINT payment_requests_ai_credit_user_id_fkey FOREIGN KEY (ai_credit_user_id)
                REFERENCES public.users(user_id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_payment_requests_ai_credit_user
    ON public.payment_requests(ai_credit_user_id);

COMMENT ON COLUMN public.payment_requests.ai_credit_package_id
    IS 'Goi AI credit (phase = ai_credit); NULL cho payment request cua booking.';
COMMENT ON COLUMN public.payment_requests.ai_credit_user_id
    IS 'Tai khoan nhan credit (phase = ai_credit); user_id la nguoi tra tien.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Admin-tunable booking bonus (dùng chung system_configs key-value có sẵn).
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO public.system_configs (config_key, config_value, description, updated_at)
VALUES ('ai_credit_bonus_on_booking', '50',
        'So luot AI credit tang cho hoc sinh moi khi co booking. Admin chinh duoc.',
        CURRENT_TIMESTAMP)
ON CONFLICT (config_key) DO NOTHING;

COMMIT;

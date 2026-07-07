-- =====================================================
-- Migration: Full production sync to match dev DB + current backend code
-- Date: 2026-07-06
-- Verified against a full production schema dump via automated column-level
-- diff against AGORADbContext.cs mappings (not eyeballed). This file replaces
-- running V20260625 + V20260706 separately -- it is their combined content.
--
-- Contents:
--   Part A (additive, safe, run first -- no backend coordination needed):
--     A0. tutor_profiles.is_accepting_bookings   (was V20260625 -- missing on prod)
--     A1. withdrawal_requests.wallet_id -> wallets
--     A2. question_bank table            (RAG knowledge source for AI)
--     A3. ai_credit_transactions ledger + users.ai_credits_balance cache
--     A4. dispute_evidences table         (separate from learning_materials)
--     A5. widen tutor_certificates.verification_status (20->50, matches code's
--         HasMaxLength(50); current values fit in 20 today but this removes a
--         latent truncation risk for free -- widening a varchar never fails)
--     A6. CHECK constraint on ai_credit_transactions.source (defense in depth,
--         same pattern as withdrawal_scores.decision in this schema)
--   Part B (BREAKING -- run only together with the new backend code deploy):
--     B1. lessons -> class_sessions, lesson_reports -> class_session_reports
--
-- Notes:
--   - table/column RENAME is used (Postgres auto-updates FKs); constraint,
--     index and sequence *names* are intentionally left unchanged (cosmetic
--     only). C# entity property names map via EF HasColumnName in
--     AGORADbContext.cs, independent of the physical column name.
--   - GRANT statements are NOT needed: this DB has
--     `ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ...
--     TO tutora_k12` already configured, so new tables/columns created by the
--     postgres role inherit the grant automatically (verified against this
--     project's own migration history: V20260604 created gradelevels/
--     tutorsubjectgradeprices with zero GRANT statements, yet both show up
--     with the grant already applied in the current production dump).
--   - Part A is fully additive/backward-compatible and can be deployed
--     independently, any time.
--   - Part B is a BREAKING rename: API routes and JSON response fields
--     change in the new backend code. Do NOT run Part B until the new
--     backend build is deployed (old code still queries `lessons` and will
--     error immediately after Part B runs if deployed out of sync).
-- =====================================================


-- =====================================================
-- PART A0: tutor_profiles.is_accepting_bookings (missing V20260625)
-- =====================================================
BEGIN;

ALTER TABLE public.tutor_profiles
    ADD COLUMN IF NOT EXISTS is_accepting_bookings boolean NOT NULL DEFAULT true;

COMMIT;


-- =====================================================
-- PART A1: withdrawal_requests.wallet_id -> wallets
-- =====================================================
BEGIN;

ALTER TABLE public.withdrawal_requests
    ADD COLUMN IF NOT EXISTS wallet_id integer NULL;

-- Backfill from the 1:1 user_id -> wallets relationship
UPDATE public.withdrawal_requests wr
SET wallet_id = w.wallet_id
FROM public.wallets w
WHERE w.user_id = wr.user_id
  AND wr.wallet_id IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'withdrawal_requests_walletid_fkey'
          AND conrelid = 'public.withdrawal_requests'::regclass
    ) THEN
        ALTER TABLE public.withdrawal_requests
            ADD CONSTRAINT withdrawal_requests_walletid_fkey
            FOREIGN KEY (wallet_id) REFERENCES public.wallets(wallet_id);
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_withdrawal_requests_walletid
    ON public.withdrawal_requests USING btree (wallet_id);

-- user_id is KEPT for backward-compat; wallet_id is now the explicit "withdraw from which wallet" pointer.
-- Not setting NOT NULL yet: historical rows for users without a wallet row would fail.
-- Once confirmed every row has wallet_id populated, a follow-up migration can add NOT NULL.

COMMIT;


-- =====================================================
-- PART A2: question_bank (RAG knowledge source for AI)
-- =====================================================
BEGIN;

CREATE SEQUENCE IF NOT EXISTS public.question_bank_id_seq
    INCREMENT BY 1 MINVALUE 1 MAXVALUE 2147483647 START 1 CACHE 1 NO CYCLE;

CREATE TABLE IF NOT EXISTS public.question_bank (
    question_id     integer NOT NULL DEFAULT nextval('question_bank_id_seq'::regclass),
    subject_id      integer NULL,
    grade_level_id  integer NULL,
    content         text NOT NULL,
    answer          text NULL,
    metadata        jsonb NULL,
    embedding       jsonb NULL, -- placeholder; upgrade to `vector(N)` (pgvector) later if real similarity search is needed
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamp DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at      timestamp DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT question_bank_pkey PRIMARY KEY (question_id),
    CONSTRAINT question_bank_subjectid_fkey FOREIGN KEY (subject_id)
        REFERENCES public.subjects(subject_id) ON DELETE SET NULL,
    CONSTRAINT question_bank_gradelevelid_fkey FOREIGN KEY (grade_level_id)
        REFERENCES public.grade_levels(grade_level_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_question_bank_subject_grade
    ON public.question_bank USING btree (subject_id, grade_level_id) WHERE (is_active = true);

COMMENT ON TABLE public.question_bank IS 'Ngan hang cau hoi/kien thuc lam nguon RAG cho AI giai bai tap. Khong phai ngan hang de mock-test.';

COMMIT;


-- =====================================================
-- PART A3: AI credit ledger + balance cache on users
-- =====================================================
BEGIN;

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS ai_credits_balance integer NOT NULL DEFAULT 0;

CREATE SEQUENCE IF NOT EXISTS public.ai_credit_transactions_id_seq
    INCREMENT BY 1 MINVALUE 1 MAXVALUE 2147483647 START 1 CACHE 1 NO CYCLE;

CREATE TABLE IF NOT EXISTS public.ai_credit_transactions (
    transaction_id  integer NOT NULL DEFAULT nextval('ai_credit_transactions_id_seq'::regclass),
    user_id         varchar(50) NOT NULL,
    amount          integer NOT NULL,        -- negative = spent, positive = granted/purchased/reset
    balance_after   integer NOT NULL,
    source          varchar(30) NOT NULL,    -- 'solve' | 'grant' | 'purchase' | 'daily_reset'
    reference_id    varchar(50) NULL,        -- e.g. chat_sessions.session_id
    description     text NULL,
    created_at      timestamp DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT ai_credit_transactions_pkey PRIMARY KEY (transaction_id),
    CONSTRAINT ai_credit_transactions_userid_fkey FOREIGN KEY (user_id)
        REFERENCES public.users(user_id) ON DELETE CASCADE,
    CONSTRAINT chk_ai_credit_transactions_source CHECK (
        (source)::text = ANY (ARRAY['solve', 'grant', 'purchase', 'daily_reset'])
    )
);

CREATE INDEX IF NOT EXISTS idx_ai_credit_transactions_user_createdat
    ON public.ai_credit_transactions USING btree (user_id, created_at DESC);

COMMENT ON TABLE public.ai_credit_transactions IS 'Ledger audit cho viec tru/cap AI credit. users.ai_credits_balance la cache cho truy van nhanh.';
COMMENT ON COLUMN public.ai_credit_transactions.amount IS 'Am = tieu (vd goi AI solve), duong = cap/nap/reset dinh ky.';

COMMIT;


-- =====================================================
-- PART A4: dispute_evidences (separate from learning_materials)
-- =====================================================
BEGIN;

CREATE SEQUENCE IF NOT EXISTS public.dispute_evidences_id_seq
    INCREMENT BY 1 MINVALUE 1 MAXVALUE 2147483647 START 1 CACHE 1 NO CYCLE;

CREATE TABLE IF NOT EXISTS public.dispute_evidences (
    dispute_evidence_id integer NOT NULL DEFAULT nextval('dispute_evidences_id_seq'::regclass),
    dispute_id          integer NOT NULL,
    uploaded_by         varchar(50) NULL,
    file_url            text NOT NULL,
    file_type           varchar(50) NULL,
    description         text NULL,
    created_at          timestamp DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT dispute_evidences_pkey PRIMARY KEY (dispute_evidence_id),
    CONSTRAINT dispute_evidences_disputeid_fkey FOREIGN KEY (dispute_id)
        REFERENCES public.disputes(dispute_id) ON DELETE CASCADE,
    CONSTRAINT dispute_evidences_uploadedby_fkey FOREIGN KEY (uploaded_by)
        REFERENCES public.users(user_id)
);

CREATE INDEX IF NOT EXISTS idx_dispute_evidences_dispute
    ON public.dispute_evidences USING btree (dispute_id);

COMMENT ON TABLE public.dispute_evidences IS 'Bang chung khieu nai (anh/file) upload rieng cho dispute. Khac voi learning_materials (tai lieu hoc tap).';

-- disputes.evidence (jsonb) column is kept as-is for backward-compat with existing rows.

COMMIT;


-- =====================================================
-- PART A5: widen tutor_certificates.verification_status (20 -> 50)
-- Current values ('verified'/8, 'pending_review'/14, 'rejected'/8) all fit
-- in 20 chars today, but code's EF mapping declares HasMaxLength(50) --
-- widening removes a latent truncation risk if a longer status is ever
-- introduced. Widening a varchar is always safe (never fails, never
-- truncates existing data).
-- =====================================================
BEGIN;

ALTER TABLE public.tutor_certificates
    ALTER COLUMN verification_status TYPE varchar(50);

COMMIT;


-- =====================================================
-- PART B1: lessons -> class_sessions (BREAKING)
-- Run this block ONLY when the new backend code is deployed at the same time.
-- =====================================================
BEGIN;

ALTER TABLE public.lessons RENAME TO class_sessions;
ALTER TABLE public.class_sessions RENAME COLUMN lesson_id TO class_session_id;
ALTER TABLE public.class_sessions RENAME COLUMN original_lesson_id TO original_session_id;

-- FK columns in child tables pointing at the renamed PK
ALTER TABLE public.disputes RENAME COLUMN lesson_id TO class_session_id;
ALTER TABLE public.feedbacks RENAME COLUMN lesson_id TO class_session_id;

ALTER TABLE public.lesson_reports RENAME TO class_session_reports;
ALTER TABLE public.class_session_reports RENAME COLUMN lesson_id TO class_session_id;

-- Table comment on public.lessons carries over automatically with RENAME.

COMMIT;

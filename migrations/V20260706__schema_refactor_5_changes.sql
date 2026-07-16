-- =====================================================
-- Migration: 5 schema fixes (semantics + missing tables/fields)
-- Date: 2026-07-06
-- Contents:
--   Part A (additive, safe, run first):
--     A1. withdrawal_requests.wallet_id -> wallets  (withdraw source is explicit now)
--     A2. question_bank table            (RAG knowledge source for AI)
--     A3. ai_credit_transactions ledger + users.ai_credits_balance cache
--     A4. dispute_evidences table         (separate from learning_materials)
--   Part B (BREAKING, run only after backend + FE are ready to deploy together):
--     B1. lessons -> class_sessions, lesson_reports -> class_session_reports
--
-- Notes:
--   - Following the convention from V20260621__rename_schema_to_snake_case.sql:
--     table/column RENAME is used (Postgres auto-updates FKs); constraint,
--     index and sequence *names* are intentionally left unchanged (cosmetic-only,
--     does not affect correctness). C# entity property names are handled via
--     EF HasColumnName mapping in AGORADbContext.cs.
--   - Part A is fully additive/backward-compatible and can be deployed independently.
--   - Part B is a BREAKING rename: API routes and JSON response fields change.
--     Coordinate backend deploy + FE deploy together before/along with running Part B.
-- =====================================================


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
        REFERENCES public.users(user_id) ON DELETE CASCADE
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
-- PART B1: lessons -> class_sessions (BREAKING)
-- Run this block ONLY when backend (Change 1 code) + FE are deployed together.
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

-- Table comment already on public.lessons ("Lessons la lich hoc that cua booking...") carries over automatically with RENAME.

COMMIT;

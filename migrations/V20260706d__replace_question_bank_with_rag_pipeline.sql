-- =====================================================
-- Migration: Replace question_bank with source_documents + questions
-- Date: 2026-07-06
-- Contents:
--   Part A (additive, safe, run first):
--     A1. pgvector extension (real similarity search, question_bank.embedding
--         was only a jsonb placeholder — see its own comment)
--     A2. source_documents table  (1 uploaded PDF = 1 row, drives an AI
--         extraction job; status tracks that job, not any individual question)
--     A3. questions table         (1 row per extracted question; replaces
--         question_bank with source traceability + a real review workflow)
--     A4. trigger to auto-maintain questions.content_hash
--     A5. match_questions() — hybrid search (hard filters + cosine rerank)
--         for the AI solver to draw on, matching question_bank's original
--         "RAG knowledge source for AI" purpose but with real vector search.
--   Part B (BREAKING — drops question_bank, run only after confirming no
--     backend code path still writes to it and any needed rows are copied):
--     B1. drop question_bank + its sequence
--
-- Notes:
--   - question_bank was added by V20260706c and has zero EF entity / repository
--     / service referencing it in the current backend — this migration assumes
--     it is empty in every real environment. Part B includes a guard that
--     RAISES instead of silently dropping data if rows are ever found.
--   - subject_id / grade_level_id use REAL foreign keys to public.subjects /
--     public.grade_levels (same database, same convention question_bank used)
--     — these are NOT cross-database references.
--   - uploaded_by / reviewed_by / created_by are intentionally NOT foreign keys
--     to users(user_id): staff/admin accounts can be hard-deleted (see the
--     ghost-user cleanup job), and a content-authorship column should not
--     block or cascade-delete published questions if that happens.
--   - Following this project's convention (V20260621), REFERENCES/renames are
--     used directly; constraint/index names are new since this is a new table,
--     not a rename.
-- =====================================================


-- =====================================================
-- PART A1: pgvector extension (best-effort — see fallback note below)
-- =====================================================
BEGIN;

-- pgvector khong phai luc nao cung co san (server tu host thieu package,
-- hoac chua duoc bat tren Supabase). KHONG de loi nay lam fail ca migration
-- — chi bao NOTICE va tiep tuc; questions.embedding dung jsonb tam thoi
-- (xem comment tren cot embedding trong Part A3).
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS vector;
    RAISE NOTICE 'pgvector san sang - nho doi questions.embedding sang vector(768) va viet lai match_questions_candidates() de rerank cosine ngay trong SQL.';
EXCEPTION
    WHEN OTHERS THEN
        RAISE NOTICE 'pgvector KHONG co san tren Postgres nay (%). embedding dung jsonb tam thoi - can cai extension tren server (self-host) hoac bat trong Database > Extensions (Supabase) truoc khi nang cap.', SQLERRM;
END
$$;

COMMIT;


-- =====================================================
-- PART A2: source_documents (1 uploaded PDF = 1 AI extraction job)
-- =====================================================
BEGIN;

CREATE TABLE IF NOT EXISTS public.source_documents (
    id                      uuid NOT NULL DEFAULT gen_random_uuid(),
    file_url                text NOT NULL,
    file_name               text NOT NULL,
    page_count              integer NULL,
    default_subject_id      integer NULL,
    default_grade_level_id  integer NULL,
    status                  text NOT NULL DEFAULT 'pending',
    questions_extracted     integer NOT NULL DEFAULT 0,
    error_message           text NULL,
    uploaded_by             varchar(50) NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT source_documents_pkey PRIMARY KEY (id),
    CONSTRAINT source_documents_status_check
        CHECK (status IN ('pending', 'processing', 'done', 'failed')),
    -- Defense in depth only — primary enforcement ("reject if > 20 pages") is
    -- application-side at upload time, before pypdf even lets the row exist.
    CONSTRAINT source_documents_page_count_check
        CHECK (page_count IS NULL OR page_count <= 20),
    CONSTRAINT source_documents_defaultsubjectid_fkey FOREIGN KEY (default_subject_id)
        REFERENCES public.subjects(subject_id) ON DELETE SET NULL,
    CONSTRAINT source_documents_defaultgradelevelid_fkey FOREIGN KEY (default_grade_level_id)
        REFERENCES public.grade_levels(grade_level_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_source_documents_status
    ON public.source_documents USING btree (status);

COMMENT ON TABLE public.source_documents IS
    'PDF nguon cho pipeline extract cau hoi bang AI. 1 file = 1 lan goi AI (status theo file, khong theo tung cau hoi). Thay the question_bank.';

COMMIT;


-- =====================================================
-- PART A3: questions (1 row per extracted question, replaces question_bank)
-- =====================================================
BEGIN;

CREATE TABLE IF NOT EXISTS public.questions (
    id                  uuid NOT NULL DEFAULT gen_random_uuid(),
    subject_id          integer NOT NULL,
    grade_level_id      integer NOT NULL,
    chapter             text NULL,
    problem_type        text NULL,
    difficulty          smallint NULL,
    content             text NOT NULL,
    solution            text NULL,
    solution_source     text NULL,
    source_document_id  uuid NULL,
    source_page         integer NULL,
    review_status       text NOT NULL DEFAULT 'pending_review',
    reviewed_by         varchar(50) NULL,
    reviewed_at         timestamptz NULL,
    content_hash        text NOT NULL DEFAULT '',
    embedded_hash        text NULL,
    embedding           jsonb NULL,
    created_by          varchar(50) NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT questions_pkey PRIMARY KEY (id),
    CONSTRAINT questions_review_status_check
        CHECK (review_status IN ('pending_review', 'published', 'rejected')),
    CONSTRAINT questions_difficulty_check
        CHECK (difficulty IS NULL OR difficulty BETWEEN 1 AND 5),
    CONSTRAINT questions_subjectid_fkey FOREIGN KEY (subject_id)
        REFERENCES public.subjects(subject_id),
    CONSTRAINT questions_gradelevelid_fkey FOREIGN KEY (grade_level_id)
        REFERENCES public.grade_levels(grade_level_id),
    CONSTRAINT questions_sourcedocumentid_fkey FOREIGN KEY (source_document_id)
        REFERENCES public.source_documents(id) ON DELETE SET NULL
);

-- Filter cứng: subject + grade + chapter (dùng trước khi rerank cosine trong match_questions()).
CREATE INDEX IF NOT EXISTS idx_questions_subject_grade_chapter
    ON public.questions USING btree (subject_id, grade_level_id, chapter);

-- Staff dashboard: đếm/lọc theo trạng thái duyệt.
CREATE INDEX IF NOT EXISTS idx_questions_review_status
    ON public.questions USING btree (review_status);

-- Backlog cho worker re-embed: chỉ những câu có content_hash lệch embedded_hash.
CREATE INDEX IF NOT EXISTS idx_questions_needs_reembed
    ON public.questions (id)
    WHERE (content_hash IS DISTINCT FROM embedded_hash);

-- HNSW cosine index — HOAN LAI: can pgvector (embedding kieu vector, khong
-- phai jsonb) truoc khi tao duoc index nay. Xem Part A1 + TODO tren cot
-- embedding. Khi nang cap, them lai:
--   CREATE INDEX idx_questions_embedding_hnsw
--       ON public.questions USING hnsw (embedding vector_cosine_ops);

COMMENT ON TABLE public.questions IS
    'Cau hoi da duoc AI extract tu source_documents, staff duyet qua review_status truoc khi dung lam RAG cho AI giai bai. Thay the question_bank.';
COMMENT ON COLUMN public.questions.review_status IS
    'pending_review (AI vua extract) -> published | rejected (staff quyet dinh). Cot DUY NHAT quyet dinh hien thi — khong dung is_published song song de tranh 2 cot lech nhau.';
COMMENT ON COLUMN public.questions.content_hash IS
    'Hash cua (content, solution) — trigger tu dong tinh lai moi khi 1 trong 2 cot doi.';
COMMENT ON COLUMN public.questions.embedded_hash IS
    'content_hash tai thoi diem lan embed gan nhat. Lech voi content_hash = can re-embed.';
COMMENT ON COLUMN public.questions.embedding IS
    'TAM THOI luu jsonb (mang float) vi pgvector chua co san — xem Part A1. Rerank cosine phai lam o application code cho toi khi nang cap sang vector(768) + HNSW index.';

COMMIT;


-- =====================================================
-- PART A4: trigger auto-maintain questions.content_hash
-- =====================================================
BEGIN;

CREATE OR REPLACE FUNCTION public.fn_questions_set_content_hash()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    -- Unit separator (\x1f) giữa content/solution để tránh trùng hash khi
    -- nội dung bị "dịch" qua ranh giới 2 cột (vd content='AB'+solution='C'
    -- không được hash giống content='A'+solution='BC').
    NEW.content_hash := md5(COALESCE(NEW.content, '') || E'\x1f' || COALESCE(NEW.solution, ''));
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_questions_set_content_hash ON public.questions;

CREATE TRIGGER trg_questions_set_content_hash
    BEFORE INSERT OR UPDATE OF content, solution ON public.questions
    FOR EACH ROW
    EXECUTE FUNCTION public.fn_questions_set_content_hash();

COMMENT ON FUNCTION public.fn_questions_set_content_hash() IS
    'Tu dong tinh lai content_hash khi content/solution doi. embedded_hash KHONG duoc trigger nay cham vao — worker embed rieng se set embedded_hash = content_hash sau khi embed xong.';

COMMIT;


-- =====================================================
-- PART A5: match_questions_candidates() — hard-filter retrieval
-- TAM THOI thay cho match_questions() thuc su (can pgvector de rerank cosine
-- ngay trong SQL — xem Part A1). Ham nay chi loc cung + tra ve embedding
-- jsonb cho application code tu tinh cosine va rerank o phia AI service.
-- Khi nang cap pgvector: doi embedding sang vector(768), viet lai ham nay
-- (co the doi ten thanh match_questions) de ORDER BY embedding <=> query
-- ngay trong SQL nhu ban dau du dinh.
-- =====================================================
BEGIN;

CREATE OR REPLACE FUNCTION public.match_questions_candidates(
    p_subject_id      integer DEFAULT NULL,
    p_grade_level_id  integer DEFAULT NULL,
    p_chapter         text    DEFAULT NULL,
    p_limit           integer DEFAULT 200
)
RETURNS TABLE (
    id              uuid,
    content         text,
    solution        text,
    subject_id      integer,
    grade_level_id  integer,
    chapter         text,
    embedding       jsonb
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        q.id,
        q.content,
        q.solution,
        q.subject_id,
        q.grade_level_id,
        q.chapter,
        q.embedding
    FROM public.questions q
    WHERE q.review_status = 'published'
      AND q.embedding IS NOT NULL
      AND (p_subject_id IS NULL OR q.subject_id = p_subject_id)
      AND (p_grade_level_id IS NULL OR q.grade_level_id = p_grade_level_id)
      AND (p_chapter IS NULL OR q.chapter = p_chapter)
    LIMIT p_limit;
$$;

COMMENT ON FUNCTION public.match_questions_candidates(integer, integer, text, integer) IS
    'TAM THOI: chi loc cung subject_id/grade_level_id/chapter, KHONG rerank cosine (pgvector chua co san). Caller (AI service) tu tinh cosine tren cot embedding (jsonb) va rerank o application code.';

COMMIT;


-- =====================================================
-- PART B1: drop question_bank (BREAKING — run only after confirming empty)
-- =====================================================
BEGIN;

-- Safety guard: chi kiem tra neu question_bank thuc su ton tai (migration
-- V20260706c tao no co the chua tung duoc chay tren DB nay). Neu khong ton
-- tai thi bo qua luon, khong co gi de drop. Neu co ton tai va co du lieu,
-- RAISE thay vi am tham xoa.
DO $$
DECLARE
    v_row_count integer;
BEGIN
    IF to_regclass('public.question_bank') IS NULL THEN
        RAISE NOTICE 'question_bank khong ton tai tren DB nay - bo qua, khong co gi de drop.';
        RETURN;
    END IF;

    SELECT count(*) INTO v_row_count FROM public.question_bank;
    IF v_row_count > 0 THEN
        RAISE EXCEPTION
            'question_bank has % row(s) — copy them into public.questions manually before running Part B.',
            v_row_count;
    END IF;
END
$$;

DROP TABLE IF EXISTS public.question_bank;
DROP SEQUENCE IF EXISTS public.question_bank_id_seq;

COMMIT;

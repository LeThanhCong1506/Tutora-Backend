-- =====================================================
-- Question bank: bảng chapters + question_types (staff CRUD) + difficulty enum
-- =====================================================
-- Mục tiêu clone GauthMath: phân loại câu hỏi dễ quản lý / scale / filter.
--
--  chapters       : chương học, GẮN với (subject_id, grade_level_id) -> ràng buộc
--                   môn+lớp gián tiếp. slug để query, name để hiển thị.
--  question_types : loại câu hỏi (Trắc nghiệm, Tự luận, Ghép đôi...), toàn cục,
--                   staff tự thêm. slug + name.
--  difficulty     : 4 cấp Bộ GD (NHAN_BIET/THONG_HIEU/VAN_DUNG/VAN_DUNG_CAO) —
--                   CỐ ĐỊNH, dùng enum text (không tách bảng).

BEGIN;

-- ── chapters ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.chapters (
    id              serial PRIMARY KEY,
    subject_id      integer NOT NULL REFERENCES public.subjects(subject_id),
    grade_level_id  integer NOT NULL REFERENCES public.grade_levels(grade_level_id),
    slug            varchar(120) NOT NULL,               -- ung_dung_dao_ham (query/ổn định)
    name            varchar(255) NOT NULL,               -- Ứng dụng đạo hàm (hiển thị)
    display_order   integer NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    -- slug là duy nhất TRONG 1 (môn, lớp) — cùng slug được phép ở lớp khác.
    CONSTRAINT uq_chapters_subject_grade_slug UNIQUE (subject_id, grade_level_id, slug)
);

CREATE INDEX IF NOT EXISTS idx_chapters_subject_grade
    ON public.chapters (subject_id, grade_level_id) WHERE is_active = true;

-- ── question_types ────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.question_types (
    id             serial PRIMARY KEY,
    slug           varchar(80) NOT NULL UNIQUE,          -- trac_nghiem, tu_luan, ghep_doi...
    name           varchar(120) NOT NULL,                -- Trắc nghiệm, Tự luận...
    display_order  integer NOT NULL DEFAULT 0,
    is_active      boolean NOT NULL DEFAULT true,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

-- ── questions: thêm FK chapter_id + question_type_id, đổi difficulty sang text ──
ALTER TABLE public.questions
    ADD COLUMN IF NOT EXISTS chapter_id       integer REFERENCES public.chapters(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS question_type_id integer REFERENCES public.question_types(id) ON DELETE SET NULL;

-- difficulty: smallint (1-5) -> text enum. Cột cũ chưa dùng thật -> drop + tạo lại.
ALTER TABLE public.questions DROP COLUMN IF EXISTS difficulty;
ALTER TABLE public.questions
    ADD COLUMN difficulty varchar(20)
    CHECK (difficulty IS NULL OR difficulty IN ('NHAN_BIET','THONG_HIEU','VAN_DUNG','VAN_DUNG_CAO'));

CREATE INDEX IF NOT EXISTS idx_questions_chapter ON public.questions (chapter_id);
CREATE INDEX IF NOT EXISTS idx_questions_type    ON public.questions (question_type_id);

COMMENT ON COLUMN public.questions.difficulty IS
    '4 cấp Bộ GD: NHAN_BIET | THONG_HIEU | VAN_DUNG | VAN_DUNG_CAO';

COMMIT;

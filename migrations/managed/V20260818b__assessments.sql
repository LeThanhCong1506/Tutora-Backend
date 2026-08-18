-- Bộ đề đánh giá đầu vào: assessments + assessment_questions.
-- TÁCH khỏi public.questions có chủ đích: questions là pool RAG của AI giải bài, gộp vào
-- đó thì AI trích nguyên đề và lộ đáp án qua semantic search. Bảng này không bao giờ embed.
-- Đề = danh sách câu cố định chọn tay (không blueprint).

BEGIN;

-- assessments — 1 row = 1 bộ đề
CREATE TABLE IF NOT EXISTS public.assessments (
    id                  uuid          NOT NULL DEFAULT gen_random_uuid(),
    title               varchar(255)  NOT NULL,
    description         text          NULL,

    subject_id          integer       NOT NULL,
    grade_level_id      integer       NOT NULL,

    -- question_count = số câu học sinh PHẢI làm. Cho phép < số câu đã gán (pool lớn hơn,
    -- rút ngẫu nhiên khi phát đề); NULL = làm hết mọi câu đã gán.
    question_count      integer       NULL,
    duration_minutes    integer       NULL,   -- NULL = không giới hạn thời gian
    -- Ngưỡng % điểm coi là "đạt". Dùng để phân mức trình độ ở bước sau.
    pass_score_percent  numeric(5,2)  NULL,
    shuffle_questions   boolean       NOT NULL DEFAULT false,
    shuffle_options     boolean       NOT NULL DEFAULT false,
    -- Cho học sinh xem đáp án đúng + giải thích sau khi submit.
    show_result         boolean       NOT NULL DEFAULT true,

    status              varchar(20)   NOT NULL DEFAULT 'draft',

    created_by          varchar(50)   NULL,
    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT assessments_pkey PRIMARY KEY (id),
    CONSTRAINT assessments_status_check
        CHECK (status IN ('draft', 'published', 'archived')),
    CONSTRAINT assessments_question_count_check
        CHECK (question_count IS NULL OR question_count > 0),
    CONSTRAINT assessments_duration_check
        CHECK (duration_minutes IS NULL OR duration_minutes > 0),
    CONSTRAINT assessments_pass_score_check
        CHECK (pass_score_percent IS NULL OR (pass_score_percent >= 0 AND pass_score_percent <= 100)),
    CONSTRAINT assessments_subjectid_fkey FOREIGN KEY (subject_id)
        REFERENCES public.subjects(subject_id),
    CONSTRAINT assessments_gradelevelid_fkey FOREIGN KEY (grade_level_id)
        REFERENCES public.grade_levels(grade_level_id),
    CONSTRAINT assessments_createdby_fkey FOREIGN KEY (created_by)
        REFERENCES public.users(user_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_assessments_subject_grade
    ON public.assessments (subject_id, grade_level_id);
CREATE INDEX IF NOT EXISTS idx_assessments_status
    ON public.assessments (status);

COMMENT ON TABLE public.assessments IS
    'Bộ đề đánh giá (placement test) — staff soạn ở CMS tab "Bộ đề đánh giá". Học sinh làm
     trước khi dùng AI giải bài để hệ thống xác định trình độ. KHÔNG liên quan pool RAG.';
COMMENT ON COLUMN public.assessments.question_count IS
    'Số câu học sinh phải làm. NULL = làm hết câu đã gán. Nếu nhỏ hơn số câu đã gán thì
     rút ngẫu nhiên (đề dùng chung 1 pool cố định, không phải blueprint theo tiêu chí).';
COMMENT ON COLUMN public.assessments.status IS
    'draft (đang soạn, học sinh không thấy) -> published -> archived. Cột DUY NHẤT quyết
     định đề có phát cho học sinh — không dùng is_active song song.';
COMMENT ON COLUMN public.assessments.pass_score_percent IS
    'Ngưỡng % điểm coi là đạt. Dùng cho bước phân mức trình độ / sinh lộ trình (làm sau).';

-- assessment_questions — câu hỏi của đề. RIÊNG, không dùng public.questions.
CREATE TABLE IF NOT EXISTS public.assessment_questions (
    id                  uuid          NOT NULL DEFAULT gen_random_uuid(),
    assessment_id       uuid          NOT NULL,

    display_order       integer       NOT NULL DEFAULT 0,
    points              numeric(6,2)  NOT NULL DEFAULT 1,

    -- Cách chấm; suy từ question_types.slug khi lưu (xem QuestionTypeFormatMapper).
    question_format     varchar(20)   NOT NULL,

    chapter_id          integer       NULL,
    -- FK -> question_types: phân loại nghiệp vụ do staff quản lý ở CMS. Khác
    -- question_format (enum cứng, quyết định cách chấm). Null nếu chưa gán.
    question_type_id    integer       NULL,
    difficulty          varchar(20)   NULL,

    content             text          NOT NULL,
    -- [{"key":"A","text":"..."}, ...]. Rỗng/NULL với short_answer.
    answer_options      jsonb         NULL,
    correct_answer      text          NULL,
    -- Chỉ dùng cho short_answer: mảng jsonb các đáp án khác cũng tính đúng
    -- (vd ["0.5", "1/2"]). So khớp không phân biệt hoa/thường, trim khoảng trắng.
    accepted_answers    jsonb         NULL,
    explanation         text          NULL,
    -- URL ảnh (Cloudinary) kèm câu — hình vẽ, bảng biến thiên.
    image_urls          jsonb         NOT NULL DEFAULT '[]'::jsonb,

    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT assessment_questions_pkey PRIMARY KEY (id),
    CONSTRAINT assessment_questions_assessmentid_fkey FOREIGN KEY (assessment_id)
        REFERENCES public.assessments(id) ON DELETE CASCADE,
    CONSTRAINT assessment_questions_chapterid_fkey FOREIGN KEY (chapter_id)
        REFERENCES public.chapters(id) ON DELETE SET NULL,
    CONSTRAINT assessment_questions_questiontypeid_fkey FOREIGN KEY (question_type_id)
        REFERENCES public.question_types(id) ON DELETE SET NULL,
    CONSTRAINT assessment_questions_format_check
        CHECK (question_format IN ('single_choice', 'multi_choice', 'true_false', 'short_answer')),
    CONSTRAINT assessment_questions_difficulty_check
        CHECK (difficulty IS NULL OR difficulty IN ('NHAN_BIET', 'THONG_HIEU', 'VAN_DUNG', 'VAN_DUNG_CAO')),
    CONSTRAINT assessment_questions_points_check
        CHECK (points > 0),
        CONSTRAINT assessment_questions_correct_answer_required
        CHECK (correct_answer IS NOT NULL AND btrim(correct_answer) <> ''),
    CONSTRAINT assessment_questions_options_required
        CHECK (
            question_format = 'short_answer'
            OR (answer_options IS NOT NULL AND jsonb_array_length(answer_options) >= 2)
        )
);

CREATE INDEX IF NOT EXISTS idx_assessment_questions_assessment
    ON public.assessment_questions (assessment_id, display_order);
CREATE INDEX IF NOT EXISTS idx_assessment_questions_chapter
    ON public.assessment_questions (chapter_id);

COMMENT ON TABLE public.assessment_questions IS
    'Câu hỏi thuộc 1 bộ đề đánh giá. TÁCH KHỎI public.questions có chủ đích: questions là
     pool RAG của AI giải bài, nếu gộp thì AI sẽ trích nguyên đề + lộ đáp án qua semantic
     search. Bảng này KHÔNG có cột embedding và không bao giờ được embed.';
COMMENT ON COLUMN public.assessment_questions.question_format IS
    'single_choice | multi_choice | true_false | short_answer — enum CỨNG, quyết định UI
     nhập đáp án và thuật toán chấm. Khác question_type_id (phân loại nghiệp vụ staff tự thêm).';
COMMENT ON COLUMN public.assessment_questions.correct_answer IS
    'single_choice -> 1 key ("A"). multi_choice / true_false -> CSV các key đúng ("A,C").
     short_answer -> chuỗi đáp án. Với true_false, key KHÔNG xuất hiện nghĩa là mệnh đề sai.';
COMMENT ON COLUMN public.assessment_questions.accepted_answers IS
    'Chỉ short_answer: mảng jsonb các cách viết khác cũng tính đúng, vd ["0.5","1/2"].';

CREATE OR REPLACE FUNCTION public.fn_assessments_touch_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_assessments_updated_at ON public.assessments;
CREATE TRIGGER trg_assessments_updated_at
    BEFORE UPDATE ON public.assessments
    FOR EACH ROW EXECUTE FUNCTION public.fn_assessments_touch_updated_at();

DROP TRIGGER IF EXISTS trg_assessment_questions_updated_at ON public.assessment_questions;
CREATE TRIGGER trg_assessment_questions_updated_at
    BEFORE UPDATE ON public.assessment_questions
    FOR EACH ROW EXECUTE FUNCTION public.fn_assessments_touch_updated_at();

COMMIT;

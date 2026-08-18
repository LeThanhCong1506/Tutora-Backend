-- Học sinh làm bài đánh giá + profile trình độ do AI kết luận.
-- KHÔNG có cờ đạt/không đạt: đề không có ngưỡng điểm, toàn bộ kết quả thô đi cho AI phân
-- tích. BE chỉ chấm để có dữ kiện khách quan.
-- Khoá theo users.user_id (giống student_topic_signals/ai_credit), không dùng student_profiles.

BEGIN;

-- assessment_attempts — 1 row = 1 lần học sinh làm 1 đề
CREATE TABLE IF NOT EXISTS public.assessment_attempts (
    id                  uuid          NOT NULL DEFAULT gen_random_uuid(),
    assessment_id       uuid          NOT NULL,
    user_id             varchar(50)   NOT NULL,

    status              varchar(20)   NOT NULL DEFAULT 'in_progress',

    started_at          timestamptz   NOT NULL DEFAULT now(),
    submitted_at        timestamptz   NULL,
    -- Chốt tại lúc bắt đầu từ assessments.duration_minutes. NULL = đề không giới hạn giờ.
    -- Snapshot chứ không đọc lại cấu hình đề: staff sửa đề giữa lúc học sinh đang làm
    -- thì bài đang làm không được đổi deadline.
    expires_at          timestamptz   NULL,

    total_questions     integer       NOT NULL DEFAULT 0,
    correct_count       integer       NOT NULL DEFAULT 0,
    earned_points       numeric(8,2)  NOT NULL DEFAULT 0,
    max_points          numeric(8,2)  NOT NULL DEFAULT 0,
    score_percent       numeric(5,2)  NULL,
    duration_seconds    integer       NULL,

    analysis_status     varchar(20)   NOT NULL DEFAULT 'pending',
    analysis_summary    text          NULL,
    analysis_result     jsonb         NULL,
    analysis_error      text          NULL,
    analyzed_at         timestamptz   NULL,

    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT assessment_attempts_pkey PRIMARY KEY (id),
    CONSTRAINT assessment_attempts_assessmentid_fkey FOREIGN KEY (assessment_id)
        REFERENCES public.assessments(id) ON DELETE CASCADE,
    CONSTRAINT assessment_attempts_userid_fkey FOREIGN KEY (user_id)
        REFERENCES public.users(user_id) ON DELETE CASCADE,
    CONSTRAINT assessment_attempts_status_check
        CHECK (status IN ('in_progress', 'submitted', 'abandoned')),
    CONSTRAINT assessment_attempts_analysis_status_check
        CHECK (analysis_status IN ('pending', 'processing', 'done', 'failed')),
    CONSTRAINT assessment_attempts_score_check
        CHECK (score_percent IS NULL OR (score_percent >= 0 AND score_percent <= 100)),
    CONSTRAINT assessment_attempts_submitted_at_check
        CHECK ((status = 'submitted') = (submitted_at IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS idx_assessment_attempts_user_created
    ON public.assessment_attempts (user_id, created_at DESC);
-- Chặn học sinh mở 2 bài dở cùng 1 đề (mở tab mới -> phải tiếp tục bài cũ).
CREATE UNIQUE INDEX IF NOT EXISTS uq_assessment_attempts_one_in_progress
    ON public.assessment_attempts (assessment_id, user_id)
    WHERE status = 'in_progress';
CREATE INDEX IF NOT EXISTS idx_assessment_attempts_analysis_pending
    ON public.assessment_attempts (submitted_at)
    WHERE status = 'submitted' AND analysis_status IN ('pending', 'failed');
CREATE INDEX IF NOT EXISTS idx_assessment_attempts_assessment
    ON public.assessment_attempts (assessment_id, status);

COMMENT ON TABLE public.assessment_attempts IS
    '1 lần học sinh làm 1 bộ đề đánh giá. BE chấm điểm khách quan; AI phân tích kết quả thô
     rồi ghi vào analysis_result + student_proficiency_profiles. KHÔNG có khái niệm đạt/không đạt.';
COMMENT ON COLUMN public.assessment_attempts.expires_at IS
    'Snapshot deadline tại lúc bắt đầu (started_at + assessments.duration_minutes). Không đọc
     lại cấu hình đề khi nộp — staff sửa đề giữa lúc đang làm không được đổi deadline bài đó.';
COMMENT ON COLUMN public.assessment_attempts.score_percent IS
    'Số đo earned/max*100. KHÔNG so với ngưỡng nào — đề không có ngưỡng đạt.';
COMMENT ON COLUMN public.assessment_attempts.analysis_result IS
    'Kết quả AI phân tích có cấu trúc (mức độ theo chương, lỗ hổng, đề xuất lộ trình). jsonb
     vì schema còn thay đổi theo prompt và luôn đọc/ghi nguyên khối.';
COMMENT ON COLUMN public.assessment_attempts.analysis_status IS
    'pending -> processing -> done | failed. ĐỘC LẬP với việc chấm điểm: AI lỗi thì bài vẫn
     đã nộp và đã có điểm.';

-- assessment_attempt_answers — câu trả lời từng câu
CREATE TABLE IF NOT EXISTS public.assessment_attempt_answers (
    id                  uuid          NOT NULL DEFAULT gen_random_uuid(),
    attempt_id          uuid          NOT NULL,
    question_id         uuid          NOT NULL,

    -- Học sinh chọn/nhập gì. Cùng định dạng correct_answer của câu: CSV key với các loại
    -- trắc nghiệm, chuỗi tự do với short_answer. NULL = bỏ trống không trả lời.
    given_answer        text          NULL,
    is_correct          boolean       NOT NULL DEFAULT false,
    earned_points       numeric(6,2)  NOT NULL DEFAULT 0,

    -- SNAPSHOT phân loại của câu tại lúc làm bài. Cố ý trùng lặp với assessment_questions:
    -- staff sửa chương/độ khó của câu về sau thì phân tích cũ vẫn phản ánh đúng đề học
    -- sinh đã làm, và câu bị xoá cũng không làm mất dữ kiện phân tích.
    chapter_id          integer       NULL,
    chapter_slug        varchar(120)  NULL,
    difficulty          varchar(20)   NULL,
    question_format     varchar(20)   NULL,

    time_spent_seconds  integer       NULL,

    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT assessment_attempt_answers_pkey PRIMARY KEY (id),
    CONSTRAINT assessment_attempt_answers_attemptid_fkey FOREIGN KEY (attempt_id)
        REFERENCES public.assessment_attempts(id) ON DELETE CASCADE,
    -- SET NULL chứ không CASCADE: xoá câu khỏi đề KHÔNG được làm mất bài làm đã nộp.
    CONSTRAINT assessment_attempt_answers_questionid_fkey FOREIGN KEY (question_id)
        REFERENCES public.assessment_questions(id) ON DELETE CASCADE,
    CONSTRAINT uq_assessment_attempt_answers UNIQUE (attempt_id, question_id)
);

CREATE INDEX IF NOT EXISTS idx_assessment_attempt_answers_attempt
    ON public.assessment_attempt_answers (attempt_id);
CREATE INDEX IF NOT EXISTS idx_assessment_attempt_answers_chapter
    ON public.assessment_attempt_answers (chapter_id, is_correct);

COMMENT ON TABLE public.assessment_attempt_answers IS
    'Câu trả lời từng câu trong 1 lần làm bài. Giữ SNAPSHOT chương/độ khó/loại câu để phân
     tích cũ không bị sai lệch khi staff sửa đề về sau.';
COMMENT ON COLUMN public.assessment_attempt_answers.given_answer IS
    'Cùng định dạng correct_answer của câu: CSV key với trắc nghiệm, chuỗi với trả lời ngắn.
     NULL = học sinh bỏ trống (khác với trả lời sai — AI phân biệt 2 trường hợp này).';

-- student_proficiency_profiles — trình độ học sinh theo môn (AI ghi)
CREATE TABLE IF NOT EXISTS public.student_proficiency_profiles (
    id                  uuid          NOT NULL DEFAULT gen_random_uuid(),
    user_id             varchar(50)   NOT NULL,
    subject_id          integer       NOT NULL,
    grade_level_id      integer       NULL,

    level               varchar(20)   NULL,
    summary             text          NULL,
    strengths           jsonb         NULL,
    weaknesses          jsonb         NULL,
    recommended_path    jsonb         NULL,

    source_attempt_id   uuid          NULL,
    attempt_count       integer       NOT NULL DEFAULT 0,

    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT student_proficiency_profiles_pkey PRIMARY KEY (id),
    CONSTRAINT student_proficiency_profiles_userid_fkey FOREIGN KEY (user_id)
        REFERENCES public.users(user_id) ON DELETE CASCADE,
    CONSTRAINT student_proficiency_profiles_subjectid_fkey FOREIGN KEY (subject_id)
        REFERENCES public.subjects(subject_id),
    CONSTRAINT student_proficiency_profiles_gradelevelid_fkey FOREIGN KEY (grade_level_id)
        REFERENCES public.grade_levels(grade_level_id),
    CONSTRAINT student_proficiency_profiles_attemptid_fkey FOREIGN KEY (source_attempt_id)
        REFERENCES public.assessment_attempts(id) ON DELETE SET NULL,
    CONSTRAINT student_proficiency_profiles_level_check
        CHECK (level IS NULL OR level IN ('beginner', 'developing', 'proficient', 'advanced')),
    CONSTRAINT uq_student_proficiency_profiles UNIQUE (user_id, subject_id)
);

CREATE INDEX IF NOT EXISTS idx_student_proficiency_profiles_user
    ON public.student_proficiency_profiles (user_id);

COMMENT ON TABLE public.student_proficiency_profiles IS
    'Trình độ học sinh theo môn, do AI kết luận từ các lần làm đề đánh giá. Bảng này được ĐỌC
     mỗi lần học sinh hỏi bài (AI giải bài tập) hoặc xin đề xuất lộ trình, để câu trả lời khớp
     thực lực. 1 row / (user, môn) — cập nhật đè sau mỗi lần đánh giá mới.';
COMMENT ON COLUMN public.student_proficiency_profiles.level IS
    'beginner | developing | proficient | advanced — AI kết luận, dùng để điều chỉnh giọng và
     độ sâu lời giải. KHÔNG suy ra từ ngưỡng điểm nào (đề không có ngưỡng đạt).';
COMMENT ON COLUMN public.student_proficiency_profiles.summary IS
    'Diễn giải ngắn để nhồi vào prompt của AI giải bài, vd "vững đại số, yếu hình không gian".';
COMMENT ON COLUMN public.student_proficiency_profiles.recommended_path IS
    'Lộ trình học AI đề xuất (thứ tự chương + lý do). Đọc lại khi học sinh xin lộ trình.';

DROP TRIGGER IF EXISTS trg_assessment_attempts_updated_at ON public.assessment_attempts;
CREATE TRIGGER trg_assessment_attempts_updated_at
    BEFORE UPDATE ON public.assessment_attempts
    FOR EACH ROW EXECUTE FUNCTION public.fn_assessments_touch_updated_at();

DROP TRIGGER IF EXISTS trg_assessment_attempt_answers_updated_at ON public.assessment_attempt_answers;
CREATE TRIGGER trg_assessment_attempt_answers_updated_at
    BEFORE UPDATE ON public.assessment_attempt_answers
    FOR EACH ROW EXECUTE FUNCTION public.fn_assessments_touch_updated_at();

DROP TRIGGER IF EXISTS trg_student_proficiency_profiles_updated_at ON public.student_proficiency_profiles;
CREATE TRIGGER trg_student_proficiency_profiles_updated_at
    BEFORE UPDATE ON public.student_proficiency_profiles
    FOR EACH ROW EXECUTE FUNCTION public.fn_assessments_touch_updated_at();

COMMIT;

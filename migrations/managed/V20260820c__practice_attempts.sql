-- Vòng luyện tập sau khi giải bài: học sinh làm 1 câu tương tự từ question bank.
--
-- Mục đích KÉP:
--  1. Chống chép bài — giải xong thì luyện lại, không chỉ copy đáp án.
--  2. Sinh tín hiệu ĐÚNG/SAI khách quan gắn với chương. student_topic_signals chỉ ghi
--     được "học sinh đã HỎI về chương nào" — hỏi không phải một quan sát về năng lực.
--     Bảng này mới cho biết em ấy LÀM ĐƯỢC hay không, tức là dữ liệu để hồ sơ sống.

BEGIN;

CREATE TABLE IF NOT EXISTS public.practice_attempts (
    id             uuid         NOT NULL DEFAULT gen_random_uuid(),
    user_id        varchar(50)  NOT NULL,
    question_id    uuid         NOT NULL,

    -- Snapshot: sửa/xoá câu trong bank sau này không làm sai lệch thống kê cũ.
    chapter        varchar(120) NULL,
    grade_level_id integer      NULL,
    difficulty     varchar(20)  NULL,

    given_answer   text         NULL,       -- NULL = bỏ qua, không trả lời
    is_correct     boolean      NOT NULL DEFAULT false,

    -- Phiên hỏi bài dẫn tới lượt luyện này. NULL = luyện từ lộ trình.
    source_session_id uuid      NULL,

    created_at     timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'),

    CONSTRAINT practice_attempts_pkey PRIMARY KEY (id),
    CONSTRAINT practice_attempts_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (user_id) ON DELETE CASCADE,
    CONSTRAINT practice_attempts_question_fk FOREIGN KEY (question_id)
        REFERENCES public.questions (id) ON DELETE CASCADE
);

-- Đường nóng: đếm đúng/sai theo chương của 1 học sinh để dựng hồ sơ sống.
CREATE INDEX IF NOT EXISTS idx_practice_attempts_user_chapter
    ON public.practice_attempts (user_id, chapter, created_at DESC);

-- Không mời lại đúng câu học sinh vừa làm.
CREATE INDEX IF NOT EXISTS idx_practice_attempts_user_question
    ON public.practice_attempts (user_id, question_id);

COMMENT ON TABLE public.practice_attempts IS
    '1 lượt luyện 1 câu từ question bank. Nguồn tín hiệu đúng/sai KHÁCH QUAN cho hồ sơ
     trình độ — khác student_topic_signals vốn chỉ ghi được học sinh đã hỏi chương nào.';

COMMIT;

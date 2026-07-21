-- Migration: Question votes (like/dislike) cho trang Tài nguyên
-- Date: 2026-07-22
-- Purpose: Người dùng like/dislike câu hỏi mẫu trong question bank. Từ đó tính
--          % hữu ích (helpful_percent = like / (like + dislike) * 100)
--          - Mỗi user chỉ 1 vote / câu (unique question_id + user_id).
--          - Không thể đổi like <-> dislike được.
--          - KHÔNG denormalize count vào bảng questions: đếm động khi list
--            (số câu published nhỏ). Thêm cột đếm sau nếu cần tối ưu.

CREATE TABLE IF NOT EXISTS public.question_votes (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    question_id uuid NOT NULL,
    user_id     character varying(50) NOT NULL,
    -- 1 = like, -1 = dislike. CHECK chặn giá trị lạ.
    vote        smallint NOT NULL,
    created_at  timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT question_votes_vote_check CHECK (vote IN (1, -1)),
    CONSTRAINT question_votes_question_fk
        FOREIGN KEY (question_id) REFERENCES public.questions (id) ON DELETE CASCADE,
    CONSTRAINT question_votes_user_fk
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE,
    -- Chống vote trùng: 1 user 1 vote / câu.
    CONSTRAINT question_votes_unique UNIQUE (question_id, user_id)
);

-- List theo câu -> đếm like/dislike: index theo question_id.
CREATE INDEX IF NOT EXISTS idx_question_votes_question
    ON public.question_votes (question_id);

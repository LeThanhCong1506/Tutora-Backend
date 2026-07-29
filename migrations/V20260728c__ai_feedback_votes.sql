-- Migration: like/dislike cho lời giải AI và gợi ý gia sư
-- Date: 2026-07-28
-- Purpose: Cả hai tính năng đều dựa trên SUY ĐOÁN của mô hình (lời giải do LLM sinh,
--          gợi ý gia sư suy từ "hỏi nhiều bài cùng chương = đang vướng"). Suy đoán thì
--          sai được, mà hiện KHÔNG có cách nào biết mình sai ở đâu.
--          Vote + lý do là vòng phản hồi duy nhất để đo và hiệu chỉnh.

-- ── Vote cho lời giải AI ──────────────────────────────
CREATE TABLE IF NOT EXISTS public.ai_message_votes (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id uuid NOT NULL,
    user_id    character varying(50) NOT NULL,
    -- 1 = like, -1 = dislike.
    vote       smallint NOT NULL,
    -- Slug lý do, chỉ có khi dislike: sai_dap_an | kho_hieu | sai_lop | thieu_buoc | khac
    reason     character varying(60),
    -- Ô "chia sẻ chi tiết" — tuỳ chọn, người dùng gõ tay.
    detail     text,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT ai_message_votes_vote_check CHECK (vote IN (1, -1)),
    CONSTRAINT ai_message_votes_message_fk
        FOREIGN KEY (message_id) REFERENCES public.chat_histories (message_id) ON DELETE CASCADE,
    CONSTRAINT ai_message_votes_user_fk
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE,
    -- 1 user 1 vote / tin nhắn; đổi ý thì UPDATE chứ không thêm dòng.
    CONSTRAINT ai_message_votes_unique UNIQUE (message_id, user_id)
);

-- Thống kê tỉ lệ dislike theo lý do (đường đọc chính của bảng này).
CREATE INDEX IF NOT EXISTS idx_ai_message_votes_reason
    ON public.ai_message_votes (vote, reason);

-- ── Vote cho gia sư được gợi ý ────────────────────────
CREATE TABLE IF NOT EXISTS public.tutor_suggestion_votes (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Định danh một LƯỢT gợi ý (BE sinh mỗi lần trả kết quả). Không FK: chỉ dùng để
    -- nhóm các vote cùng một lần hiển thị, không có bảng lượt gợi ý riêng.
    suggestion_id uuid NOT NULL,
    session_id    uuid,
    tutor_id      character varying(50) NOT NULL,
    user_id       character varying(50) NOT NULL,
    vote          smallint NOT NULL,
    -- sai_mon_chuong | gia_cao | khong_hop_lich | khong_can_gia_su | khac
    reason        character varying(60),
    detail        text,
    -- Chương yếu tại thời điểm gợi ý. Không có cột này thì dislike chỉ là con số vô
    -- nghĩa — có nó mới trả lời được "gợi ý sai ở chương nào".
    chapter_slug  character varying(120),
    created_at    timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT tutor_suggestion_votes_vote_check CHECK (vote IN (1, -1)),
    CONSTRAINT tutor_suggestion_votes_user_fk
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE,
    CONSTRAINT tutor_suggestion_votes_unique UNIQUE (suggestion_id, tutor_id, user_id)
);

-- "Gợi ý sai ở chương nào" — truy vấn hiệu chỉnh thuật toán.
CREATE INDEX IF NOT EXISTS idx_tutor_suggestion_votes_chapter
    ON public.tutor_suggestion_votes (chapter_slug, vote);

-- Gia sư nào hay bị dislike (có thể do hồ sơ/giá, không phải do thuật toán).
CREATE INDEX IF NOT EXISTS idx_tutor_suggestion_votes_tutor
    ON public.tutor_suggestion_votes (tutor_id, vote);

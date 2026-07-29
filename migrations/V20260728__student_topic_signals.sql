-- Migration: student_topic_signals — tín hiệu chủ đề học sinh đã giải
-- Date: 2026-07-28
-- Purpose: Mỗi lần giải bài, classifier (tutora-ai) sinh grade/chapter/topic/confidence.
--          Trước đây bị vứt đi. Nay lưu lại để phát hiện "chương đang vướng" ->
--          gợi ý gia sư.

CREATE TABLE IF NOT EXISTS public.student_topic_signals (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      character varying(50) NOT NULL,
    session_id   uuid NOT NULL,
    message_id   uuid,
    grade        character varying(50),
    -- Slug chương MOET, khớp public.chapters.slug (vd "can_bac_hai").
    chapter_slug character varying(120) NOT NULL,
    -- dai_so | giai_tich | hinh_hoc_phang | hinh_hoc_khong_gian | xac_suat_thong_ke
    topic        character varying(60),
    -- Do LLM tự chấm nên kém hiệu chuẩn; chỉ dùng để LỌC thô, không dùng làm trọng số chính.
    confidence   real NOT NULL DEFAULT 0,
    created_at   timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT student_topic_signals_user_fk
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE,
    CONSTRAINT student_topic_signals_session_fk
        FOREIGN KEY (session_id) REFERENCES public.chat_sessions (session_id) ON DELETE CASCADE
);

-- Đường nóng: đếm chương TRONG 1 PHIÊN (gợi ý tính theo phiên, không gom lịch sử).
CREATE INDEX IF NOT EXISTS idx_student_topic_signals_session
    ON public.student_topic_signals (session_id, chapter_slug);

-- Phụ: tổng hợp theo user qua thời gian (báo cáo tuần / đề luyện — làm sau).
CREATE INDEX IF NOT EXISTS idx_student_topic_signals_user_created
    ON public.student_topic_signals (user_id, created_at DESC);

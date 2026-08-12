-- chat_sessions.session_type có sẵn CHECK constraint chỉ cho phép 'homework'/'tutor_matching'
-- (đặt từ trước, ngoài phạm vi V20260810b__class_session_ai_jobs.sql). SessionType mới
-- ChatSessionType.VideoSummary = 'video_summary' (chat hỏi tiếp về tóm tắt video buổi học)
-- bị chặn bởi constraint này -> mọi INSERT chat_sessions cho tính năng này lỗi 23514.

BEGIN;

ALTER TABLE chat_sessions DROP CONSTRAINT IF EXISTS chk_chat_sessions_type;

ALTER TABLE chat_sessions
    ADD CONSTRAINT chk_chat_sessions_type
    CHECK ((session_type)::text = ANY (ARRAY['homework', 'tutor_matching', 'video_summary']::text[]));

COMMIT;

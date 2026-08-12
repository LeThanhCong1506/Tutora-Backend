-- Gemini phân tích video buổi học: tóm tắt gửi học sinh (student_summary) + tự động điền
-- báo cáo gửi gia sư (tutor_report_fill). GeminiFileUri cache lại để 2 loại job của cùng
-- buổi học không phải upload lại video nhiều lần (Gemini giữ file đã upload ~48h).

BEGIN;

CREATE TABLE IF NOT EXISTS class_session_ai_jobs (
    job_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    class_session_id INTEGER NOT NULL,
    job_type VARCHAR(30) NOT NULL,
    requested_by_user_id VARCHAR(50) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    result_text TEXT,
    result_json JSONB,
    gemini_file_uri TEXT,
    gemini_file_name TEXT,
    gemini_file_expires_at TIMESTAMP WITHOUT TIME ZONE,
    error_message TEXT,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    completed_at TIMESTAMP WITHOUT TIME ZONE,
    CONSTRAINT class_session_ai_jobs_session_fkey
        FOREIGN KEY (class_session_id)
        REFERENCES class_sessions (class_session_id)
        ON DELETE CASCADE,
    CONSTRAINT class_session_ai_jobs_type_check
        CHECK (job_type IN ('student_summary', 'tutor_report_fill')),
    CONSTRAINT class_session_ai_jobs_status_check
        CHECK (status IN ('pending', 'processing', 'completed', 'failed'))
);

CREATE INDEX IF NOT EXISTS idx_class_session_ai_jobs_session
    ON class_session_ai_jobs (class_session_id, job_type);

-- Chặn bấm nút 2 lần tạo 2 job chạy song song cho cùng buổi học + loại job.
CREATE UNIQUE INDEX IF NOT EXISTS ux_class_session_ai_jobs_active
    ON class_session_ai_jobs (class_session_id, job_type)
    WHERE status IN ('pending', 'processing');

-- Chat hỏi tiếp về tóm tắt video: chat_sessions vốn không gắn với buổi học nào cả
-- (chỉ homework/tutor_matching, scope theo user). Thêm cột này để tra nhanh
-- "đã có phiên chat video_summary cho (user, classSessionId) chưa".
ALTER TABLE chat_sessions
    ADD COLUMN IF NOT EXISTS class_session_id INTEGER;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chat_sessions_class_session_fkey'
    ) THEN
        ALTER TABLE chat_sessions
            ADD CONSTRAINT chat_sessions_class_session_fkey
            FOREIGN KEY (class_session_id)
            REFERENCES class_sessions (class_session_id)
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_chat_sessions_class_session
    ON chat_sessions (class_session_id)
    WHERE class_session_id IS NOT NULL;

COMMIT;

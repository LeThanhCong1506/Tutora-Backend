-- Gemini trả cả tóm tắt lẫn bản chép lời (transcript) đầy đủ trong CÙNG 1 lần gọi generateContent
-- (không tốn thêm lượt phân tích video riêng) — transcript hiển thị ở tab riêng cạnh tóm tắt cho học sinh.

BEGIN;

ALTER TABLE class_session_ai_jobs
    ADD COLUMN IF NOT EXISTS transcript_text TEXT;

COMMIT;

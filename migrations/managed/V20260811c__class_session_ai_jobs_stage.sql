-- Giai đoạn con của job student_summary lúc Processing (analyzing -> verifying) — chỉ để FE hiện
-- đúng thông báo "đang làm gì" cho học sinh, không phải trạng thái chính (status vẫn giữ nguyên).

BEGIN;

ALTER TABLE class_session_ai_jobs
    ADD COLUMN IF NOT EXISTS stage TEXT;

COMMIT;

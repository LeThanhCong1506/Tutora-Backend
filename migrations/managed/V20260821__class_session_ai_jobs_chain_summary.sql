-- Tóm tắt AI cho cả chuỗi buổi học bị ngắt/nối (bù/phụ/học lại): tổng hợp text-only từ các
-- tóm tắt student_summary từng buổi trong chuỗi, không upload video riêng cho job này.
-- Xem ClassSessionVideoAiService.RunChainSummaryJobAsync.

BEGIN;

ALTER TABLE class_session_ai_jobs
    DROP CONSTRAINT class_session_ai_jobs_type_check;

ALTER TABLE class_session_ai_jobs
    ADD CONSTRAINT class_session_ai_jobs_type_check
        CHECK (job_type IN ('student_summary', 'tutor_report_fill', 'chain_summary'));

COMMIT;

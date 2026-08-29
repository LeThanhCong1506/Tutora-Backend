-- Job "làm nóng" cache GeminiFileUri: chạy ngay khi video relay xong lên Drive (RecordingRelayService),
-- trước khi học sinh/gia sư bấm tóm tắt/điền báo cáo. Xem ClassSessionVideoAiService.PrewarmGeminiFileAsync.

BEGIN;

ALTER TABLE class_session_ai_jobs
    DROP CONSTRAINT class_session_ai_jobs_type_check;

ALTER TABLE class_session_ai_jobs
    ADD CONSTRAINT class_session_ai_jobs_type_check
        CHECK (job_type IN ('student_summary', 'tutor_report_fill', 'chain_summary', 'prewarm'));

COMMIT;

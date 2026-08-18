-- Bỏ ngưỡng đạt: đề đánh giá không phân đạt/không đạt, kết quả thô đi hết cho AI phân tích.
-- File mới thay vì sửa V20260818b vì migration đã apply bị khoá theo checksum SHA256 của
-- cả file. LƯU Ý: mỗi file chỉ được 1 khối BEGIN...COMMIT, ở đầu và cuối.

BEGIN;

ALTER TABLE public.assessments
    DROP CONSTRAINT IF EXISTS assessments_pass_score_check;

ALTER TABLE public.assessments
    DROP COLUMN IF EXISTS pass_score_percent;

COMMENT ON TABLE public.assessments IS
    'Bộ đề đánh giá (placement test) — staff soạn ở CMS tab "Bộ đề đánh giá". Học sinh làm
     trước khi dùng AI giải bài để hệ thống xác định trình độ. KHÔNG liên quan pool RAG.
     CỐ Ý KHÔNG có cột ngưỡng đạt: kết quả thô đi hết cho AI phân tích, xem
     assessment_attempts + student_proficiency_profiles.';

COMMIT;

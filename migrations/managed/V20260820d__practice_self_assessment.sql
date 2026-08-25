-- Vòng luyện chuyển từ TRẮC NGHIỆM sang TỰ LUẬN + học sinh tự chấm.
--
-- Vì sao đổi: câu trắc nghiệm trong bank không có sẵn correct_answer, phải trích bằng
-- LLM từ solution. Kiểm chứng chéo (giải lại độc lập 60 câu lớp 9) cho 96,7% khớp —
-- tức ~3% câu SAI đáp án. Chấm oan một học sinh làm đúng, lại kèm nhãn "đã kiểm chứng",
-- hại hơn hẳn việc để em tự đối chiếu với lời giải mẫu.
--
-- Đổi lại mất nhãn đúng/sai khách quan; bù bằng tự đánh giá — kém tin cậy hơn nhưng
-- không bao giờ chấm sai, và bản thân việc đối chiếu đã là kỹ năng đáng dạy.

BEGIN;

ALTER TABLE public.practice_attempts
    DROP COLUMN IF EXISTS given_answer,
    ADD COLUMN IF NOT EXISTS self_assessment varchar(20) NULL;

ALTER TABLE public.practice_attempts
    DROP CONSTRAINT IF EXISTS practice_attempts_self_assessment_check;

ALTER TABLE public.practice_attempts
    ADD CONSTRAINT practice_attempts_self_assessment_check
    CHECK (self_assessment IS NULL OR self_assessment IN ('correct', 'partial', 'wrong'));

COMMENT ON COLUMN public.practice_attempts.self_assessment IS
    'Học sinh tự chấm sau khi xem lời giải mẫu: correct | partial | wrong.
     is_correct chỉ true khi = ''correct'' — partial không tính là làm được.';

COMMIT;

-- Hỗ trợ MỌI loại câu hỏi. Tự luận/ghép đôi/sắp xếp lưu là 'essay': BE không auto-chấm,
-- đáp án mẫu + bài làm đi cho AI đánh giá. Học sinh luôn xem được đáp án; show_result chỉ
-- gác phần điểm.

BEGIN;

ALTER TABLE public.assessment_questions
    DROP CONSTRAINT IF EXISTS assessment_questions_format_check;

ALTER TABLE public.assessment_questions
    ADD CONSTRAINT assessment_questions_format_check
    CHECK (question_format IN ('single_choice', 'multi_choice', 'true_false', 'short_answer', 'essay'));

ALTER TABLE public.assessment_questions
    DROP CONSTRAINT IF EXISTS assessment_questions_options_required;

ALTER TABLE public.assessment_questions
    ADD CONSTRAINT assessment_questions_options_required
    CHECK (
        question_format IN ('short_answer', 'essay')
        OR (answer_options IS NOT NULL AND jsonb_array_length(answer_options) >= 2)
    );

COMMENT ON COLUMN public.assessment_questions.question_format IS
    'single_choice | multi_choice | true_false | short_answer | essay. Suy từ question_types.slug
     khi lưu (xem QuestionTypeFormatMapper). essay = không auto-chấm, AI đánh giá.';

COMMENT ON COLUMN public.assessments.show_result IS
    'Cho học sinh xem ĐIỂM sau khi nộp. Đáp án đúng + giải thích thì LUÔN xem được.';

COMMIT;

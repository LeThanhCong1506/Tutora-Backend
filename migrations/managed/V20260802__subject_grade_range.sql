-- subjects: khối lớp áp dụng theo môn học.
-- Trước đây tutor có thể chọn môn như Vật Lý/Hóa Học cho Lớp 1, 2... vì không có ràng buộc
-- nào giữa môn và khối lớp. min_grade_level_id/max_grade_level_id NULL = không giới hạn
-- (giữ nguyên hành vi cũ cho môn chưa cấu hình, vd Toán/Tiếng Anh/Ngữ Văn).

BEGIN;

ALTER TABLE public.subjects
    ADD COLUMN IF NOT EXISTS min_grade_level_id integer REFERENCES public.grade_levels(grade_level_id),
    ADD COLUMN IF NOT EXISTS max_grade_level_id integer REFERENCES public.grade_levels(grade_level_id);

COMMENT ON COLUMN public.subjects.min_grade_level_id IS
    'Khối lớp thấp nhất môn này áp dụng (so theo grade_levels.level_order). NULL = không giới hạn.';
COMMENT ON COLUMN public.subjects.max_grade_level_id IS
    'Khối lớp cao nhất môn này áp dụng (so theo grade_levels.level_order). NULL = không giới hạn.';

-- Seed phạm vi mặc định theo chương trình phổ thông VN (match theo tiền tố tên môn vì
-- subject_name không hoàn toàn cố định, vd 'Toán' hay 'Toán Học' tuỳ lần seed).
-- Toán / Tiếng Anh / Ngữ Văn: không giới hạn -> giữ NULL, không cần UPDATE.

UPDATE public.subjects s
   SET min_grade_level_id = g6.grade_level_id,
       max_grade_level_id = g12.grade_level_id
  FROM public.grade_levels g6, public.grade_levels g12
 WHERE g6.level_order = 6 AND g12.level_order = 12
   AND (
        s.subject_name LIKE 'Vật Lý%' OR
        s.subject_name LIKE 'Hóa%' OR
        s.subject_name LIKE 'Sinh%' OR
        s.subject_name LIKE 'Lịch Sử%' OR
        s.subject_name LIKE 'Địa Lý%' OR
        s.subject_name LIKE 'IELTS%'
   );

UPDATE public.subjects s
   SET min_grade_level_id = g3.grade_level_id,
       max_grade_level_id = g12.grade_level_id
  FROM public.grade_levels g3, public.grade_levels g12
 WHERE g3.level_order = 3 AND g12.level_order = 12
   AND s.subject_name LIKE 'Tin Học%';

COMMIT;

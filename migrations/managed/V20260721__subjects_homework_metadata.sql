-- =====================================================
-- subjects: metadata cho app AI Homework Helper (apps.tutora.vn)
-- =====================================================
-- 4 cột thêm vào:
--   slug                 - định danh URL-friendly (vd 'toan-hoc'), giống bảng chapters.
--   icon_url             - ảnh icon môn (Cloudinary). NULL -> FE fallback icon mặc định.
--   is_homework_enabled  - cờ DƯƠNG: true = mở trong app giải bài tập.
--                          Default FALSE: môn mới mặc định KHOÁ, phải bật tường minh.
--   display_order        - thứ tự hiển thị, giống bảng chapters đã có.

BEGIN;

ALTER TABLE public.subjects
    ADD COLUMN IF NOT EXISTS slug text,
    ADD COLUMN IF NOT EXISTS icon_url text,
    ADD COLUMN IF NOT EXISTS is_homework_enabled boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS display_order integer NOT NULL DEFAULT 0;

COMMENT ON COLUMN public.subjects.slug IS
    'Định danh URL-friendly, chữ thường không dấu, phân tách bằng "-" (vd: toan-hoc).';
COMMENT ON COLUMN public.subjects.icon_url IS
    'URL ảnh icon môn học (Cloudinary). NULL -> client dùng icon mặc định.';
COMMENT ON COLUMN public.subjects.is_homework_enabled IS
    'true = môn được mở trong AI Homework Helper. Mặc định false (khoá).';
COMMENT ON COLUMN public.subjects.display_order IS
    'Thứ tự hiển thị, nhỏ trước. Cùng giá trị thì xếp theo subject_name.';

-- Sinh slug từ tên môn tiếng Việt.
UPDATE public.subjects
   SET slug = trim(both '-' from regexp_replace(
           regexp_replace(
               lower(translate(
                   replace(replace(subject_name, 'đ', 'd'), 'Đ', 'D'),
                   'áàảãạăắằẳẵặâấầẩẫậéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵ',
                   'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyy'
               )),
               '[^a-z0-9]+', '-', 'g'
           ),
           '-{2,}', '-', 'g'
       ))
 WHERE slug IS NULL AND subject_name IS NOT NULL;

-- Slug phải duy nhất để dùng làm định danh trong URL.
-- Partial index: bỏ qua các hàng slug NULL (subject_name NULL thì không sinh được).
CREATE UNIQUE INDEX IF NOT EXISTS ux_subjects_slug
    ON public.subjects (slug) WHERE slug IS NOT NULL;

UPDATE public.subjects
   SET is_homework_enabled = true,
       display_order = 1
 WHERE subject_name LIKE 'Toán%';

CREATE INDEX IF NOT EXISTS ix_subjects_homework_enabled
    ON public.subjects (is_homework_enabled, display_order)
    WHERE is_homework_enabled;

COMMIT;

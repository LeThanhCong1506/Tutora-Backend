-- =====================================================
-- questions.image_urls: ảnh (bảng biến thiên/đồ thị) crop từ PDF khi extract
-- =====================================================
-- 1 câu có thể nhiều ảnh. Lưu jsonb array các URL (Cloudinary). AI extract PDF
-- -> crop vùng hình -> upload -> điền mảng URL vào đây.

BEGIN;

ALTER TABLE public.questions
    ADD COLUMN IF NOT EXISTS image_urls jsonb NOT NULL DEFAULT '[]'::jsonb;

COMMENT ON COLUMN public.questions.image_urls IS
    'Mảng URL ảnh (Cloudinary) của câu — bảng biến thiên/đồ thị crop từ PDF.';

COMMIT;

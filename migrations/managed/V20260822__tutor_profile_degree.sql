-- Tách "Trình độ học vấn" (1 ô tự do gộp cả bằng cấp + trường) thành 2 khái niệm riêng:
-- Education (giữ nguyên cột, từ nay chỉ chứa TÊN TRƯỜNG) và Degree (Học vị) mới.
--
-- Dữ liệu cũ trong education (dạng "Cử nhân Sư phạm Toán - Đại học Sư phạm Hà Nội")
-- KHÔNG được tự tách ở đây — parse tự động dễ sai (nhiều định dạng khác nhau tutor tự gõ).
-- Giữ nguyên trong education, degree để trống; tutor sẽ tự điền lại khi vào sửa hồ sơ
-- lần tới (form mới bắt buộc nhập cả 2 trường).

BEGIN;

ALTER TABLE public.tutor_profiles
    ADD COLUMN IF NOT EXISTS degree varchar(100) NULL;

COMMENT ON COLUMN public.tutor_profiles.degree IS
    'Học vị (Cử nhân/Thạc sĩ/Tiến sĩ...). Tách riêng khỏi education kể từ 2026-08-22 — education giờ chỉ lưu tên trường.';

COMMIT;

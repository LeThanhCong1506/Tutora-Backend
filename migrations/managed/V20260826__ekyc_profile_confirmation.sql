-- Quét CCCD không còn tự ghi đè hồ sơ nữa: dữ liệu OCR (họ tên, ngày sinh, giới tính,
-- địa chỉ thường trú) chỉ được ghi SAU KHI chủ tài khoản xem và bấm xác nhận.
-- Cột này đánh dấu thời điểm xác nhận đó.
--
-- NULL = đã quét CCCD nhưng chưa xác nhận → hồ sơ giữ nguyên giá trị cũ, FE hiện nhắc
-- "còn thông tin CCCD chưa cập nhật". Danh tính vẫn tính là đã xác minh (is_identity_verified)
-- vì ảnh + số CCCD đã lưu và đã qua chống trùng.

BEGIN;

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS ekyc_profile_confirmed_at timestamp without time zone NULL;

COMMENT ON COLUMN public.users.ekyc_profile_confirmed_at IS
    'Thời điểm chủ tài khoản xác nhận áp dụng dữ liệu CCCD vào hồ sơ. NULL = đã quét nhưng chưa xác nhận.';

-- Backfill: tài khoản đã xác minh TRƯỚC thay đổi này đã bị luồng cũ tự đồng bộ hồ sơ rồi,
-- nên coi như đã xác nhận — nếu để NULL thì toàn bộ gia sư cũ sẽ thấy nhắc xác nhận lại
-- một thay đổi mà thực tế đã được ghi từ lâu.
UPDATE public.users
SET ekyc_profile_confirmed_at = COALESCE(created_at, CURRENT_TIMESTAMP)
WHERE is_identity_verified = true
  AND ekyc_raw_data IS NOT NULL
  AND ekyc_profile_confirmed_at IS NULL;

COMMIT;

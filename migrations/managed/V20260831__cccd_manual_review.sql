-- Xác minh CCCD tự động (OCR FPT.AI) có thể thất bại nhiều lần liên tiếp vì ảnh mờ/thiếu sáng dù
-- CCCD thật. Áp dụng cho cả Tutor lẫn Student (cùng cột DB): trước đây học sinh bị chặn cứng mỗi
-- lần thất bại (không có lối thoát), còn gia sư lại được cho qua ngay từ lần đầu (không có ngưỡng
-- nào). Giờ thống nhất 1 ngưỡng chung: đủ số lần OCR thất bại liên tiếp cho phép (xem
-- StudentIdentityService/EkycService), vẫn NHẬN ảnh và lưu lại, nhưng chuyển sang chờ Admin xem
-- thủ công thay vì tự động từ chối/chấp nhận — is_identity_verified vẫn false cho tới khi Admin
-- duyệt qua endpoint riêng.

BEGIN;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS cccd_ocr_failed_attempts integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS is_identity_pending_review boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN users.cccd_ocr_failed_attempts IS
    'Số lần OCR (FPT.AI) đọc CCCD thất bại LIÊN TIẾP kể từ lần thành công/escalate gần nhất. Reset về 0 khi OCR đọc thành công hoặc khi đã escalate sang chờ Admin duyệt thủ công.';
COMMENT ON COLUMN users.is_identity_pending_review IS
    'true khi OCR thất bại đủ ngưỡng lần liên tiếp: ảnh CCCD đã được nhận và lưu, nhưng chờ Admin xem thủ công thay vì tự động xác minh. is_identity_verified vẫn false cho tới khi Admin duyệt qua endpoint riêng.';

COMMIT;

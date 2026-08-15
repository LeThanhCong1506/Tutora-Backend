-- Gia sư giờ cũng tạo dispute được (trước đây chỉ phụ huynh/học sinh). Khi gia sư là người
-- tạo, bên phải phản hồi là phụ huynh/học sinh — cột tutor_response/tutor_responded_at hiện
-- có KHÔNG dùng lại được vì tên cột đã cố định nghĩa "phản hồi của gia sư" và vẫn cần giữ
-- đúng nghĩa đó cho chiều cũ (phụ huynh/học sinh tạo, gia sư phản hồi). Thêm cặp cột riêng
-- cho chiều ngược lại thay vì đổi tên/tái dùng cột cũ — không rủi ro dữ liệu hiện có.

BEGIN;

ALTER TABLE disputes ADD COLUMN IF NOT EXISTS respondent_response TEXT;
ALTER TABLE disputes ADD COLUMN IF NOT EXISTS respondent_responded_at TIMESTAMP WITHOUT TIME ZONE;

COMMIT;

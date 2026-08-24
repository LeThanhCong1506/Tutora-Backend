-- Cho phép gia sư + học sinh/phụ huynh CÙNG đồng ý bỏ hẳn buổi phụ (link 2, sinh ra khi buổi gốc
-- bị báo ngắt) mà không cần đợi tới nửa đêm để hệ thống tự đóng — và quan trọng hơn, cho phép gia
-- sư NỘP ĐƯỢC BÁO CÁO cho buổi gốc (nội dung phần đã dạy thật, VD 80%) ngay lúc đó, thay vì mất
-- trắng vì SubmitReportAsync trước giờ chỉ nhận status in_progress, còn buổi bị ngắt chuyển thẳng
-- sang interrupted (trạng thái cụt, không bao giờ tự quay lại in_progress được nữa).
--
-- 2 cột dưới đây gắn vào chính BUỔI PHỤ (is_continuation=true) — mỗi bên xác nhận qua 1 cột riêng;
-- khi cả 2 cùng có giá trị, ClassSessionService.SubmitReportAsync mới chấp nhận báo cáo cho buổi
-- GỐC (status=interrupted) và tự huỷ buổi phụ này trong cùng transaction.

BEGIN;

ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS tutor_skip_confirmed_at TIMESTAMP WITHOUT TIME ZONE;
ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS student_skip_confirmed_at TIMESTAMP WITHOUT TIME ZONE;

COMMIT;

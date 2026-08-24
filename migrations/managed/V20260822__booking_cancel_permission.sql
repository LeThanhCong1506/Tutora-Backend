-- Thêm quyền booking.cancel cho tính năng staff hủy booking khi phụ huynh "nghỉ ngang" (xác minh
-- qua tổng đài, thao tác trực tiếp trên CMS — POST /api/admin/bookings/{id}/cancel-ghost).
-- Không sửa migration cũ vì migration đã apply bị khoá theo checksum — quyền thêm sau bắt buộc
-- phải nằm ở file mới.

BEGIN;

INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('booking.cancel','CMS / Nghiệp vụ','Booking','Hủy','Hủy booking do phụ huynh nghỉ ngang (xác minh qua tổng đài)')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('booking.cancel','booking.view')
ON CONFLICT (permission_key, required_permission_key) DO NOTHING;

COMMIT;

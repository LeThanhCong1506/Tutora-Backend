-- Thêm quyền tutor_cccd.decide cho tính năng Admin duyệt/từ chối CCCD chờ xem thủ công (xem
-- V20260831__cccd_manual_review.sql). Không sửa migration cũ vì migration đã apply bị khoá theo
-- checksum — quyền thêm sau bắt buộc phải nằm ở file mới.

BEGIN;

INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('tutor_cccd.decide','Chuyên môn','Định danh gia sư','Duyệt','Duyệt hoặc từ chối CCCD chờ xem thủ công')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('tutor_cccd.decide','tutor_cccd.view')
ON CONFLICT (permission_key, required_permission_key) DO NOTHING;

COMMIT;

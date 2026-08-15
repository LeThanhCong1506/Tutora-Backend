-- PR #215 (3d5a796) thêm quyền support.view vào Permissions.cs cho tính năng nhắn tin hỗ trợ
-- (Supportthread/Supportmessage) nhưng quên seed permission_definitions — khiến
-- PermissionMigrationContractTests báo đỏ CI trên develop. Không sửa migration cũ vì migration
-- đã apply bị khoá theo checksum — quyền thêm sau bắt buộc phải nằm ở file mới.

BEGIN;

INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('support.view','CMS / Nghiệp vụ','Nhắn tin hỗ trợ','Xem','Xem và trả lời tin nhắn hỗ trợ gia sư/phụ huynh/học sinh')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

COMMIT;

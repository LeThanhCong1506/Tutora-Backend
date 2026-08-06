-- Quyền kiểm duyệt đánh giá cho CMS.
-- V20260716__staff_permission_groups.sql cũng đã được bổ sung hai key này để
-- PermissionMigrationContractTests xanh và DB dựng mới seed đủ, nhưng file đó đã chạy trên
-- các DB hiện có nên sẽ không tự apply lại. Migration này seed riêng cho những DB đó.
-- Idempotent: chạy lại nhiều lần không sao.

BEGIN;

INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('feedback.view','CMS / Nghiệp vụ','Đánh giá','Xem','Xem đánh giá gia sư'),
('feedback.moderate','CMS / Nghiệp vụ','Đánh giá','Kiểm duyệt','Ẩn hoặc hiện đánh giá')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('feedback.moderate','feedback.view')
ON CONFLICT DO NOTHING;

COMMIT;

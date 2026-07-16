CREATE TABLE IF NOT EXISTS permission_definitions (
    permission_key VARCHAR(100) PRIMARY KEY,
    domain VARCHAR(100) NOT NULL,
    module VARCHAR(100) NOT NULL,
    action VARCHAR(50) NOT NULL,
    label VARCHAR(200) NOT NULL
);

CREATE TABLE IF NOT EXISTS permission_definition_requirements (
    permission_key VARCHAR(100) NOT NULL REFERENCES permission_definitions(permission_key) ON DELETE CASCADE,
    required_permission_key VARCHAR(100) NOT NULL REFERENCES permission_definitions(permission_key) ON DELETE RESTRICT,
    CONSTRAINT permission_definition_requirements_pkey PRIMARY KEY (permission_key, required_permission_key),
    CONSTRAINT permission_definition_requirements_not_self CHECK (permission_key <> required_permission_key)
);

CREATE TABLE IF NOT EXISTS permission_groups (
    permission_group_id UUID PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(255),
    version BIGINT NOT NULL DEFAULT 1 CHECK (version >= 0),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_by VARCHAR(50) NOT NULL,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    updated_by VARCHAR(50) NOT NULL,
    updated_at TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    deleted_at TIMESTAMP WITHOUT TIME ZONE
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_permission_groups_active_name
    ON permission_groups (LOWER(name)) WHERE is_deleted = FALSE;

CREATE TABLE IF NOT EXISTS permission_group_permissions (
    permission_group_id UUID NOT NULL REFERENCES permission_groups(permission_group_id) ON DELETE CASCADE,
    permission_key VARCHAR(100) NOT NULL REFERENCES permission_definitions(permission_key) ON DELETE RESTRICT,
    CONSTRAINT permission_group_permissions_pkey PRIMARY KEY (permission_group_id, permission_key)
);

CREATE TABLE IF NOT EXISTS staff_permission_group_assignments (
    staff_user_id VARCHAR(50) PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
    permission_group_id UUID REFERENCES permission_groups(permission_group_id) ON DELETE RESTRICT,
    version BIGINT NOT NULL DEFAULT 0 CHECK (version >= 0),
    updated_by VARCHAR(50) NOT NULL,
    updated_at TIMESTAMP WITHOUT TIME ZONE NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_staff_permission_group_assignments_group
    ON staff_permission_group_assignments(permission_group_id);

CREATE TABLE IF NOT EXISTS permission_audit_logs (
    permission_audit_log_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    action VARCHAR(50) NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    entity_id VARCHAR(100) NOT NULL,
    permission_group_id UUID,
    staff_user_id VARCHAR(50),
    version BIGINT,
    actor_user_id VARCHAR(50) NOT NULL,
    details_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_permission_audit_logs_group ON permission_audit_logs(permission_group_id);
CREATE INDEX IF NOT EXISTS idx_permission_audit_logs_staff ON permission_audit_logs(staff_user_id);

CREATE OR REPLACE FUNCTION prevent_permission_audit_mutation()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'permission_audit_logs is append-only';
END;
$$;
DROP TRIGGER IF EXISTS trg_permission_audit_logs_append_only ON permission_audit_logs;
CREATE TRIGGER trg_permission_audit_logs_append_only
BEFORE UPDATE OR DELETE ON permission_audit_logs
FOR EACH ROW EXECUTE FUNCTION prevent_permission_audit_mutation();

INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('tutor_approval.view','Chuyên môn','Duyệt gia sư','Xem','Xem hồ sơ gia sư chờ duyệt'),
('tutor_approval.decide','Chuyên môn','Duyệt gia sư','Duyệt','Duyệt hoặc từ chối hồ sơ gia sư'),
('certificate.view','Chuyên môn','Chứng chỉ','Xem','Xem chứng chỉ chờ duyệt'),
('certificate.verify','Chuyên môn','Chứng chỉ','Duyệt','Xác minh chứng chỉ gia sư'),
('tutor_cccd.view','Chuyên môn','Định danh gia sư','Xem','Xem ảnh CCCD gia sư'),
('tutor_profile_update.view','Chuyên môn','Cập nhật hồ sơ gia sư','Xem','Xem yêu cầu cập nhật hồ sơ gia sư'),
('tutor_profile_update.decide','Chuyên môn','Cập nhật hồ sơ gia sư','Duyệt','Duyệt hoặc từ chối cập nhật hồ sơ gia sư'),
('user.view','CMS / Nghiệp vụ','Người dùng','Xem','Xem danh sách và chi tiết người dùng'),
('user.update','CMS / Nghiệp vụ','Người dùng','Sửa','Chỉnh sửa thông tin người dùng'),
('user.deactivate','CMS / Nghiệp vụ','Người dùng','Khóa','Khóa hoặc mở khóa tài khoản'),
('dashboard.view','Báo cáo','Tổng quan','Xem','Xem dashboard và báo cáo thống kê'),
('financial.view','Báo cáo','Tài chính','Xem','Xem báo cáo tài chính'),
('booking.view','CMS / Nghiệp vụ','Booking','Xem','Xem danh sách và chi tiết booking'),
('promotion.manage','CMS / Nghiệp vụ','Khuyến mãi','Quản lý','Quản lý mã khuyến mãi'),
('payout.view','Tài chính','Rút tiền','Xem','Xem yêu cầu rút tiền'),
('payout.approve','Tài chính','Rút tiền','Duyệt','Duyệt yêu cầu rút tiền'),
('payout.reject','Tài chính','Rút tiền','Từ chối','Từ chối yêu cầu rút tiền'),
('system_alert.view','Tài chính','Cảnh báo hệ thống','Xem','Xem cảnh báo hệ thống'),
('system_alert.resolve','Tài chính','Cảnh báo hệ thống','Xử lý','Xử lý cảnh báo hệ thống'),
('dispute.view','CMS / Nghiệp vụ','Tranh chấp','Xem','Xem tranh chấp'),
('dispute.investigate','CMS / Nghiệp vụ','Tranh chấp','Điều tra','Điều tra tranh chấp'),
('dispute.resolve','CMS / Nghiệp vụ','Tranh chấp','Giải quyết','Giải quyết tranh chấp'),
('warning.view','CMS / Nghiệp vụ','Cảnh cáo & đình chỉ','Xem','Xem lịch sử cảnh cáo và đình chỉ'),
('warning.create','CMS / Nghiệp vụ','Cảnh cáo & đình chỉ','Thêm','Tạo cảnh cáo người dùng'),
('suspension.manage','CMS / Nghiệp vụ','Cảnh cáo & đình chỉ','Quản lý','Đình chỉ hoặc gỡ đình chỉ tài khoản'),
('export.data','Báo cáo','Xuất dữ liệu','Xuất','Xuất dữ liệu'),
('notification.view','Khác','Thông báo','Xem','Xem thông báo hệ thống'),
('notification.send','Khác','Thông báo','Gửi','Gửi thông báo'),
('notification.delete','Khác','Thông báo','Xóa','Xóa thông báo'),
('lookup.view','Danh mục','Danh mục học tập','Xem','Xem danh mục học tập'),
('lookup.create','Danh mục','Danh mục học tập','Thêm','Thêm danh mục học tập'),
('lookup.update','Danh mục','Danh mục học tập','Sửa','Sửa danh mục học tập'),
('lookup.delete','Danh mục','Danh mục học tập','Xóa','Xóa hoặc ngừng dùng danh mục học tập'),
('question_bank.view','Nội dung','Ngân hàng câu hỏi','Xem','Xem ngân hàng câu hỏi'),
('question_bank.create','Nội dung','Ngân hàng câu hỏi','Thêm','Thêm câu hỏi'),
('question_bank.update','Nội dung','Ngân hàng câu hỏi','Sửa','Sửa câu hỏi'),
('question_bank.delete','Nội dung','Ngân hàng câu hỏi','Xóa','Xóa câu hỏi'),
('question_document.view','Nội dung','Trích xuất câu hỏi từ PDF','Xem','Xem lịch sử trích xuất PDF'),
('question_document.upload','Nội dung','Trích xuất câu hỏi từ PDF','Upload','Upload PDF và trích xuất câu hỏi')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('tutor_approval.decide','tutor_approval.view'),
('certificate.verify','certificate.view'),
('tutor_profile_update.decide','tutor_profile_update.view'),
('user.update','user.view'),
('user.deactivate','user.view'),
('payout.approve','payout.view'),
('payout.reject','payout.view'),
('system_alert.resolve','system_alert.view'),
('dispute.investigate','dispute.view'),
('dispute.resolve','dispute.view'),
('warning.create','warning.view'),
('suspension.manage','warning.view'),
('notification.send','notification.view'),
('notification.delete','notification.view'),
('lookup.create','lookup.view'),
('lookup.update','lookup.view'),
('lookup.delete','lookup.view'),
('question_bank.create','question_bank.view'),
('question_bank.update','question_bank.view'),
('question_bank.delete','question_bank.view'),
('question_document.upload','question_document.view'),
('question_document.upload','question_bank.create')
ON CONFLICT DO NOTHING;

-- Convert every distinct legacy direct-permission set into one shared Legacy group.
-- The old staff_permissions table is deliberately retained for one release as rollback data.
DO $$
BEGIN
    IF to_regclass('public.staff_permissions') IS NOT NULL THEN
        CREATE TEMP TABLE legacy_staff_sets ON COMMIT DROP AS
        SELECT u.user_id AS staff_user_id,
               string_agg(sp.permission_key, ',' ORDER BY sp.permission_key) AS signature
        FROM users u
        JOIN staff_permissions sp ON sp.user_id = u.user_id
        JOIN permission_definitions pd ON pd.permission_key = sp.permission_key
        WHERE LOWER(u.primary_role) = 'staff'
          AND NOT EXISTS (
              SELECT 1 FROM staff_permission_group_assignments a WHERE a.staff_user_id = u.user_id)
        GROUP BY u.user_id
        HAVING COUNT(*) > 0;

        CREATE TEMP TABLE legacy_group_sets ON COMMIT DROP AS
        SELECT DISTINCT signature,
               gen_random_uuid() AS permission_group_id
        FROM legacy_staff_sets;

        INSERT INTO permission_groups(
            permission_group_id, name, description, version, is_deleted,
            created_by, created_at, updated_by, updated_at)
        SELECT permission_group_id,
               'Legacy ' || SUBSTRING(md5(signature), 1, 8),
               'Tự động tạo từ tập quyền trực tiếp trước migration',
               1, FALSE, 'SYSTEM_MIGRATION', CURRENT_TIMESTAMP,
               'SYSTEM_MIGRATION', CURRENT_TIMESTAMP
        FROM legacy_group_sets;

        INSERT INTO permission_group_permissions(permission_group_id, permission_key)
        SELECT lgs.permission_group_id, key.permission_key
        FROM legacy_group_sets lgs
        CROSS JOIN LATERAL unnest(string_to_array(lgs.signature, ',')) AS key(permission_key);

        INSERT INTO staff_permission_group_assignments(
            staff_user_id, permission_group_id, version, updated_by, updated_at)
        SELECT lss.staff_user_id, lgs.permission_group_id, 1,
               'SYSTEM_MIGRATION', CURRENT_TIMESTAMP
        FROM legacy_staff_sets lss
        JOIN legacy_group_sets lgs USING (signature);

        INSERT INTO permission_audit_logs(
            action, entity_type, entity_id, permission_group_id, version,
            actor_user_id, details_json, created_at)
        SELECT 'LEGACY_GROUP_CREATED', 'PermissionGroup', permission_group_id::text,
               permission_group_id, 1, 'SYSTEM_MIGRATION',
               jsonb_build_object('permissionKeys', string_to_array(signature, ',')), CURRENT_TIMESTAMP
        FROM legacy_group_sets;

        INSERT INTO permission_audit_logs(
            action, entity_type, entity_id, permission_group_id, staff_user_id,
            version, actor_user_id, details_json, created_at)
        SELECT 'LEGACY_STAFF_ASSIGNED', 'StaffPermissionGroupAssignment', lss.staff_user_id,
               lgs.permission_group_id, lss.staff_user_id, 1, 'SYSTEM_MIGRATION',
               jsonb_build_object('previousGroupId', NULL, 'newGroupId', lgs.permission_group_id),
               CURRENT_TIMESTAMP
        FROM legacy_staff_sets lss
        JOIN legacy_group_sets lgs USING (signature);
    END IF;
END;
$$;

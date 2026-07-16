-- =====================================================
-- Migration: create staff_permissions table
-- Date: 2026-07-07
--
-- Purpose: replace the blanket "AdminOrStaff role = full admin access" model
-- with per-staff granular permissions that Admin grants explicitly.
-- =====================================================
BEGIN;

CREATE TABLE IF NOT EXISTS staff_permissions (
    user_id         VARCHAR(50)  NOT NULL,
    permission_key  VARCHAR(100) NOT NULL,
    granted_by      VARCHAR(50)  NOT NULL,
    granted_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT staff_permissions_pkey PRIMARY KEY (user_id, permission_key),
    CONSTRAINT staff_permissions_userid_fkey FOREIGN KEY (user_id)
        REFERENCES users(user_id) ON DELETE CASCADE
);

-- granted_by is intentionally NOT a hard FK to users, same convention as
-- system_alerts.resolved_by -- the granting admin's account may later be
-- hard-deleted by GhostUserCleanupJob, and we don't want that to cascade
-- into wiping the audit trail of who granted a permission.

CREATE INDEX IF NOT EXISTS idx_staff_permissions_userid ON staff_permissions(user_id);

COMMENT ON TABLE staff_permissions IS 'Danh sach quyen han cu the Admin cap cho tung Staff - thay the co che blanket AdminOrStaff role.';
COMMENT ON COLUMN staff_permissions.permission_key IS 'Phai khop MV.DomainLayer.Constants.Permissions.All';
COMMENT ON COLUMN staff_permissions.granted_by IS 'Userid Admin da cap - KHONG FK cung vi tai khoan co the bi GhostUserCleanupJob xoa';

COMMIT;

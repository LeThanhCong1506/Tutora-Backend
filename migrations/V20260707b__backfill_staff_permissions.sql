-- =====================================================
-- Migration: backfill staff_permissions for existing Staff
-- Date: 2026-07-07
--
-- Run this together with the backend deploy that swaps
-- [Authorize(Roles=AdminOrStaff)] for [RequirePermission(...)].
-- Only backfills the permissions Staff already effectively have today
-- (the old AdminOrStaff-accessible actions) -- anything that used to be
-- Admin-only is NOT backfilled; Admin grants it explicitly if desired.
-- =====================================================
BEGIN;

INSERT INTO staff_permissions (user_id, permission_key, granted_by, granted_at)
SELECT u.user_id, perm.permission_key, 'SYSTEM_BACKFILL', CURRENT_TIMESTAMP
FROM users u
CROSS JOIN (VALUES
    ('tutor_approval.view'), ('tutor_approval.decide'), ('certificate.view'),
    ('payout.view'), ('payout.approve'), ('payout.reject'),
    ('dispute.view'), ('dispute.investigate'), ('dispute.resolve'),
    ('export.data'),
    ('notification.send'), ('notification.view'), ('notification.delete')
) AS perm(permission_key)
WHERE u.primary_role = 'Staff' AND u.status = 1
ON CONFLICT (user_id, permission_key) DO NOTHING;

COMMIT;

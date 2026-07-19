-- Manual rollback companion for V20260716__staff_permission_groups.sql.
-- Run this before deploying the previous application version. It projects the
-- current group-based effective rights back into the retained legacy table.
-- New group/audit tables are deliberately kept for forensics and a later retry.
BEGIN;

DO $$
BEGIN
    IF to_regclass('public.staff_permissions') IS NULL THEN
        RAISE EXCEPTION 'staff_permissions is missing; group migration cannot be rolled back safely';
    END IF;

    -- Remove only catalog rights for Staff that already have a group-assignment
    -- row. Unknown legacy keys and users that were never migrated are retained.
    DELETE FROM staff_permissions sp
    USING staff_permission_group_assignments assignment
    WHERE sp.user_id = assignment.staff_user_id
      AND sp.permission_key IN (SELECT permission_key FROM permission_definitions);

    INSERT INTO staff_permissions(user_id, permission_key, granted_by, granted_at)
    SELECT assignment.staff_user_id,
           group_permission.permission_key,
           'SYSTEM_GROUP_ROLLBACK',
           CURRENT_TIMESTAMP
    FROM staff_permission_group_assignments assignment
    JOIN permission_groups permission_group
      ON permission_group.permission_group_id = assignment.permission_group_id
     AND permission_group.is_deleted = FALSE
    JOIN permission_group_permissions group_permission
      ON group_permission.permission_group_id = permission_group.permission_group_id
    ON CONFLICT (user_id, permission_key) DO UPDATE SET
        granted_by = EXCLUDED.granted_by,
        granted_at = EXCLUDED.granted_at;
END;
$$;

COMMIT;

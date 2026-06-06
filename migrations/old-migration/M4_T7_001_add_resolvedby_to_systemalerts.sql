-- M4-T7: Add resolvedby field to systemalerts table
-- This field tracks which admin user resolved the alert

ALTER TABLE systemalerts ADD COLUMN IF NOT EXISTS resolvedby VARCHAR(50);

COMMENT ON COLUMN systemalerts.resolvedby IS 'Admin user ID who resolved the alert';

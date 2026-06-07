-- =====================================================
-- Migration: Remove isactive column from tutoravailability
-- Date: 2026-06-07
-- Description: 
--   - Remove soft-delete pattern from tutoravailability table
--   - Delete operation will now permanently remove records
--   - Clean up existing inactive records before removing column
-- =====================================================

BEGIN;

-- Step 1: Log existing inactive records for audit purposes (optional)
DO $$
DECLARE
    inactive_count INT;
BEGIN
    SELECT COUNT(*) INTO inactive_count
    FROM tutoravailability
    WHERE isactive = false;
    
    RAISE NOTICE 'Found % inactive tutoravailability records that will be deleted', inactive_count;
END $$;

-- Step 2: Delete all inactive availability records
DELETE FROM tutoravailability
WHERE isactive = false;

-- Step 3: Drop the isactive column
ALTER TABLE tutoravailability
DROP COLUMN IF EXISTS isactive;

-- Step 4: Add comment to document the change
COMMENT ON TABLE tutoravailability IS 
'Tutor availability slots. Delete operations remove records permanently (no soft delete).';

COMMIT;

-- =====================================================
-- Verification queries (run after migration)
-- =====================================================
-- Check table structure:
-- SELECT column_name, data_type, is_nullable, column_default
-- FROM information_schema.columns
-- WHERE table_name = 'tutoravailability'
-- ORDER BY ordinal_position;

-- Count remaining records:
-- SELECT COUNT(*) FROM tutoravailability;

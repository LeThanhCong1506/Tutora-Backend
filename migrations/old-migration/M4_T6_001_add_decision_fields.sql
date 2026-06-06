-- M4_T6_001: Add decision tracking fields to withdrawalrequests
-- Purpose: Track approval decision and processor for withdrawal automation

-- Add decision tracking columns
ALTER TABLE withdrawalrequests
ADD COLUMN IF NOT EXISTS decision VARCHAR(50),
ADD COLUMN IF NOT EXISTS processedby VARCHAR(50);

-- Add index for efficient querying
CREATE INDEX IF NOT EXISTS idx_withdrawalrequests_decision
ON withdrawalrequests(decision)
WHERE decision IS NOT NULL;

-- Add comments for documentation
COMMENT ON COLUMN withdrawalrequests.decision IS 'Trust scoring decision: AUTO_APPROVE, DELAYED, MANUAL_REVIEW';
COMMENT ON COLUMN withdrawalrequests.processedby IS 'Who processed the withdrawal: SYSTEM or admin username';

-- M4_T5_001: Add PayOS tracking fields to withdrawalrequests table
-- Purpose: Track PayOS payout status, transaction IDs, and retry attempts

-- Add PayOS tracking columns
ALTER TABLE withdrawalrequests
ADD COLUMN IF NOT EXISTS payostransactionid VARCHAR(255),
ADD COLUMN IF NOT EXISTS payosstatus VARCHAR(50),
ADD COLUMN IF NOT EXISTS payosresponsecode VARCHAR(50),
ADD COLUMN IF NOT EXISTS payoserror TEXT,
ADD COLUMN IF NOT EXISTS retrycount INT DEFAULT 0,
ADD COLUMN IF NOT EXISTS lastretryat TIMESTAMP;

-- Add indexes for efficient querying
CREATE INDEX IF NOT EXISTS idx_withdrawalrequests_payostransactionid
ON withdrawalrequests(payostransactionid);

CREATE INDEX IF NOT EXISTS idx_withdrawalrequests_status_retrycount
ON withdrawalrequests(status, retrycount)
WHERE status = 'Pending';

-- Add comment for documentation
COMMENT ON COLUMN withdrawalrequests.payostransactionid IS 'PayOS payout transaction ID for tracking';
COMMENT ON COLUMN withdrawalrequests.payosstatus IS 'PayOS payout status: PENDING, SUCCESS, FAILED';
COMMENT ON COLUMN withdrawalrequests.payosresponsecode IS 'PayOS error code if payout failed';
COMMENT ON COLUMN withdrawalrequests.payoserror IS 'PayOS error message in English';
COMMENT ON COLUMN withdrawalrequests.retrycount IS 'Number of retry attempts for failed payouts';
COMMENT ON COLUMN withdrawalrequests.lastretryat IS 'Timestamp of last retry attempt';

-- M4-T4: Trust Scoring + Tiered Approval
-- Migration 001: Create withdrawal_scores table
-- Purpose: Audit trail for trust score calculations and approval decisions

CREATE TABLE IF NOT EXISTS withdrawal_scores (
    scoreid SERIAL PRIMARY KEY,
    withdrawalrequestid INTEGER NOT NULL,
    tutorid VARCHAR(50) NOT NULL,
    basescore INTEGER NOT NULL DEFAULT 50,
    positivefactors JSONB,
    negativefactors JSONB,
    fraudflags JSONB,
    totalscore INTEGER NOT NULL,
    decision VARCHAR(50) NOT NULL,
    createdat TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_withdrawal_scores_withdrawal FOREIGN KEY (withdrawalrequestid)
        REFERENCES withdrawalrequests(withdrawalid) ON DELETE CASCADE,
    CONSTRAINT fk_withdrawal_scores_tutor FOREIGN KEY (tutorid)
        REFERENCES tutorprofiles(tutorid) ON DELETE CASCADE,
    CONSTRAINT chk_totalscore_range CHECK (totalscore >= 0 AND totalscore <= 100),
    CONSTRAINT chk_decision_valid CHECK (decision IN ('AUTO_APPROVE', 'DELAYED', 'MANUAL_REVIEW', 'AUTO_REJECT'))
);

-- Indexes for performance optimization
CREATE INDEX idx_withdrawal_scores_withdrawalid ON withdrawal_scores(withdrawalrequestid);
CREATE INDEX idx_withdrawal_scores_tutorid ON withdrawal_scores(tutorid);
CREATE INDEX idx_withdrawal_scores_decision ON withdrawal_scores(decision);
CREATE INDEX idx_withdrawal_scores_tutorid_createdat ON withdrawal_scores(tutorid, createdat DESC);
CREATE INDEX idx_withdrawal_scores_createdat ON withdrawal_scores(createdat DESC);

-- Partial indexes for specific decisions (faster queries for admin dashboard)
CREATE INDEX idx_withdrawal_scores_manual_review ON withdrawal_scores(tutorid, createdat DESC)
    WHERE decision = 'MANUAL_REVIEW';
CREATE INDEX idx_withdrawal_scores_auto_reject ON withdrawal_scores(tutorid, createdat DESC)
    WHERE decision = 'AUTO_REJECT';

-- GIN indexes for JSONB queries
CREATE INDEX idx_withdrawal_scores_positivefactors ON withdrawal_scores USING GIN(positivefactors);
CREATE INDEX idx_withdrawal_scores_negativefactors ON withdrawal_scores USING GIN(negativefactors);
CREATE INDEX idx_withdrawal_scores_fraudflags ON withdrawal_scores USING GIN(fraudflags);

COMMENT ON TABLE withdrawal_scores IS 'Audit trail for trust score calculations and approval decisions';
COMMENT ON COLUMN withdrawal_scores.basescore IS 'Starting score (default 50)';
COMMENT ON COLUMN withdrawal_scores.positivefactors IS 'Array of positive factors: [{ factor, points, detail }]';
COMMENT ON COLUMN withdrawal_scores.negativefactors IS 'Array of negative factors: [{ factor, points, detail }]';
COMMENT ON COLUMN withdrawal_scores.fraudflags IS 'Array of fraud flags from fraud detection engine';
COMMENT ON COLUMN withdrawal_scores.totalscore IS 'Final score after applying all factors (clamped 0-100)';
COMMENT ON COLUMN withdrawal_scores.decision IS 'Approval decision: AUTO_APPROVE, DELAYED, MANUAL_REVIEW, AUTO_REJECT';

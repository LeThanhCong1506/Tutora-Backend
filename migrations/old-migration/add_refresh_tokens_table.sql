-- Refresh Token Implementation
-- Migration: Create refreshtokens table
-- Purpose: Store hashed refresh tokens with rotation and reuse detection support

CREATE TABLE IF NOT EXISTS refreshtokens (
    id VARCHAR(50) PRIMARY KEY,
    tokenhash VARCHAR(128) NOT NULL,
    userid VARCHAR(50) NOT NULL,
    tokenfamily VARCHAR(50) NOT NULL,
    expiresat TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    createdat TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revokedat TIMESTAMP WITHOUT TIME ZONE NULL,
    replacedbytokenhash VARCHAR(128) NULL,

    CONSTRAINT fk_refreshtokens_user FOREIGN KEY (userid)
        REFERENCES users(userid) ON DELETE CASCADE
);

-- Unique index on tokenhash for fast lookup
CREATE UNIQUE INDEX idx_refreshtokens_tokenhash ON refreshtokens(tokenhash);

-- Index for looking up all tokens by user (for revoke-all on logout)
CREATE INDEX idx_refreshtokens_userid ON refreshtokens(userid);

-- Index for token family (for reuse detection - revoke entire family)
CREATE INDEX idx_refreshtokens_tokenfamily ON refreshtokens(tokenfamily);

-- Index for cleanup job (remove expired tokens periodically)
CREATE INDEX idx_refreshtokens_expiresat ON refreshtokens(expiresat);

COMMENT ON TABLE refreshtokens IS 'Refresh tokens with rotation support. Raw tokens are never stored - only SHA256 hashes.';
COMMENT ON COLUMN refreshtokens.tokenhash IS 'SHA256 hash of the raw refresh token (hex string)';
COMMENT ON COLUMN refreshtokens.tokenfamily IS 'GUID shared by all rotated tokens from the same login session. Used to detect reuse attacks.';
COMMENT ON COLUMN refreshtokens.revokedat IS 'Set when token is rotated or explicitly revoked. NULL = still valid.';
COMMENT ON COLUMN refreshtokens.replacedbytokenhash IS 'Hash of the successor token after rotation (for audit trail).';

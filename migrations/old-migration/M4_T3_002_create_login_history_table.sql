-- M4-T3: Fraud Detection Rules Engine
-- Migration 002: Create login_history table
-- Purpose: Track user login IPs for pattern detection (multiple IPs = suspicious)

CREATE TABLE IF NOT EXISTS login_history (
    logid SERIAL PRIMARY KEY,
    userid VARCHAR(50) NOT NULL,
    ipaddress VARCHAR(45) NOT NULL,
    useragent TEXT,
    loggedat TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_login_history_user FOREIGN KEY (userid)
        REFERENCES users(userid) ON DELETE CASCADE
);

-- Indexes for pattern detection queries
CREATE INDEX idx_login_history_userid ON login_history(userid);
CREATE INDEX idx_login_history_userid_loggedat ON login_history(userid, loggedat DESC);
CREATE INDEX idx_login_history_ipaddress ON login_history(ipaddress);
CREATE INDEX idx_login_history_loggedat ON login_history(loggedat DESC);

COMMENT ON TABLE login_history IS 'Login history for fraud pattern detection (multiple IPs, unusual locations)';
COMMENT ON COLUMN login_history.ipaddress IS 'IPv4 or IPv6 address (max 45 chars for IPv6)';
COMMENT ON COLUMN login_history.useragent IS 'Browser/device user agent string';

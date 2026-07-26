-- Durable evidence that an authenticated participant reached the waiting lobby before a lesson.
--
-- One row represents one SignalR connection. The client refreshes lobby state every ~10 seconds,
-- so last_seen_at and beat_count distinguish a participant who actually waited from a tab that
-- only flashed open. A reconnect creates a new row, preserving the interruption instead of
-- rewriting history.
--
-- No foreign key to class_sessions on purpose: dispute evidence must outlive operational rows.

CREATE TABLE IF NOT EXISTS session_lobby_visits (
    lobby_visit_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    class_session_id INTEGER NOT NULL,
    app_user_id      VARCHAR(50) NOT NULL,
    role             VARCHAR(20) NOT NULL,
    connection_id    VARCHAR(128) NOT NULL,
    entered_at       TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    last_seen_at     TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    beat_count       INTEGER NOT NULL DEFAULT 1,
    left_at          TIMESTAMP WITHOUT TIME ZONE,
    -- NULL while the connection is active; 'leave' for a deliberate navigation and
    -- 'disconnect' when SignalR observed the connection disappear.
    closed_reason    VARCHAR(20)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_session_lobby_visits_connection
    ON session_lobby_visits (class_session_id, connection_id);

CREATE INDEX IF NOT EXISTS idx_session_lobby_visits_session
    ON session_lobby_visits (class_session_id, app_user_id, entered_at);

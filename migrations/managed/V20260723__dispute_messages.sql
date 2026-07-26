CREATE TABLE IF NOT EXISTS dispute_messages (
    dispute_message_id SERIAL PRIMARY KEY,
    dispute_id INT NOT NULL REFERENCES disputes(dispute_id) ON DELETE CASCADE,
    thread_type VARCHAR(20) NOT NULL,
    sender_id VARCHAR(50) REFERENCES users(user_id),
    sender_role VARCHAR(20),
    message TEXT NOT NULL,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_dispute_messages_thread ON dispute_messages (dispute_id, thread_type);

-- Chuyển tiền chủ động: admin/staff cộng thẳng vào ví một user (gia sư/phụ huynh/học sinh)
-- mà không gắn với booking hay yêu cầu rút tiền nào. Khác payout ở chỗ tiền được cộng NGAY
-- khi tạo, không qua bước duyệt/xác nhận thứ hai — bảng này chỉ giữ lại ai làm, cho ai,
-- bao nhiêu, vì sao, để đối chiếu sau này.

BEGIN;

CREATE TABLE IF NOT EXISTS admin_wallet_transfers (
    transfer_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    recipient_user_id VARCHAR(50) NOT NULL REFERENCES users(user_id) ON DELETE RESTRICT,
    amount NUMERIC(15, 2) NOT NULL CHECK (amount > 0),
    reason TEXT NOT NULL,
    created_by VARCHAR(50) NOT NULL REFERENCES users(user_id) ON DELETE RESTRICT,
    wallet_transaction_id INTEGER REFERENCES wallet_transactions(transaction_id) ON DELETE SET NULL,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_admin_wallet_transfers_recipient ON admin_wallet_transfers(recipient_user_id);

-- Quyền chuyển tiền chủ động — tách khỏi payout.approve vì đây là quyền phát sinh tiền chủ
-- động, rủi ro cao hơn hẳn việc duyệt một yêu cầu do chính user tạo ra.
INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('payout.transfer','Tài chính','Rút tiền','Chuyển tiền','Chuyển tiền chủ động cho user')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('payout.transfer','payout.view')
ON CONFLICT DO NOTHING;

COMMIT;

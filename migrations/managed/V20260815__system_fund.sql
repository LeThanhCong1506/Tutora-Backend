-- Quỹ hệ thống: nguồn tiền thật mà "Chuyển tiền chủ động" phải trừ vào, thay vì cộng thẳng
-- vào ví user mà không trừ ở đâu cả (trước đây tiền "phát sinh" ra không có gì đối ứng).
-- Nạp quỹ đòi hỏi ảnh chứng minh (giống payout thủ công) vì đây cũng là tiền thật admin xác
-- nhận đã có/đã chuẩn bị sẵn, không phải một con số tự gõ vào.

BEGIN;

CREATE TABLE IF NOT EXISTS system_fund (
    fund_id SMALLINT PRIMARY KEY DEFAULT 1,
    balance NUMERIC(15, 2) NOT NULL DEFAULT 0 CHECK (balance >= 0),
    updated_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT system_fund_singleton CHECK (fund_id = 1)
);

INSERT INTO system_fund (fund_id, balance)
VALUES (1, 0)
ON CONFLICT (fund_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS system_fund_topups (
    topup_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    amount NUMERIC(15, 2) NOT NULL CHECK (amount > 0),
    reason TEXT NOT NULL,
    proof_image_path TEXT NOT NULL,
    created_by VARCHAR(50) NOT NULL REFERENCES users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_system_fund_topups_created_by ON system_fund_topups(created_by);

-- Quyền nạp quỹ — tách khỏi payout.transfer vì đây là xác nhận tiền thật đã sẵn sàng để chi,
-- rủi ro khác hẳn việc chi tiền (transfer) vốn đã bị chặn bởi số dư quỹ hiện có.
INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('payout.fund_topup','Tài chính','Rút tiền','Nạp quỹ','Nạp quỹ hệ thống')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('payout.fund_topup','payout.view')
ON CONFLICT DO NOTHING;

COMMIT;

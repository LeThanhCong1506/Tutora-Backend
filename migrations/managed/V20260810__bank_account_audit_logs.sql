-- Audit trail cho bank_accounts (thêm/sửa/xoá). Mọi thao tác ghi vào bank_accounts từ giờ đều
-- bắt buộc qua xác thực OTP trước (BankAccountOtpService) — bảng này chỉ ghi lại đã xảy ra gì,
-- ai làm, khi nào, từ đâu, phục vụ tra cứu khi có khiếu nại/tranh chấp. Xây mới, không phải khôi
-- phục lại bảng bank_change_logs cũ (đã gỡ ở V20260708__remove_payos_payout_and_bank_verification.sql
-- — bảng đó chỉ áp dụng cho tutor và không có khái niệm action/delete).

BEGIN;

CREATE TABLE IF NOT EXISTS bank_account_audit_logs (
    bank_account_audit_log_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id                   VARCHAR(50) NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    -- Nullable + SET NULL (không CASCADE): xoá tài khoản ngân hàng không được xoá luôn dòng log
    -- vừa ghi lại chính hành động xoá đó.
    bank_account_id           INTEGER REFERENCES bank_accounts(bank_account_id) ON DELETE SET NULL,
    action                    VARCHAR(20) NOT NULL,
    old_bank_name             VARCHAR(100),
    old_account_number        VARCHAR(50),
    old_account_holder_name   VARCHAR(100),
    new_bank_name             VARCHAR(100),
    new_account_number        VARCHAR(50),
    new_account_holder_name   VARCHAR(100),
    changed_at                TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ip_address                VARCHAR(45),
    user_agent                TEXT,
    CONSTRAINT bank_account_audit_logs_action_check CHECK (action IN ('created', 'updated', 'deleted'))
);

CREATE INDEX IF NOT EXISTS idx_bank_account_audit_logs_user_changed_at
    ON bank_account_audit_logs (user_id, changed_at DESC);

COMMIT;

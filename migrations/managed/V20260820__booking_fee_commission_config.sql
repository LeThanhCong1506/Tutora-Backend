-- Commission (phí sàn) hai bên phụ huynh/gia sư hiện đang hardcode trong BookingFeeCalculator.cs
-- (ParentFeePercent/TutorFeePercent = 0.05m). Chuyển sang system_configs để admin chỉnh trực tiếp
-- từ CMS mà không cần deploy lại backend, kèm bảng lịch sử để audit mỗi lần đổi phí.

BEGIN;

INSERT INTO system_configs (config_key, config_value, description, updated_at) VALUES
    ('booking_fee_parent_percent', '0.05', 'Phí sàn cộng thêm vào giá cho phụ huynh (dạng phân số, 0.05 = 5%).', CURRENT_TIMESTAMP),
    ('booking_fee_tutor_percent', '0.05', 'Phí sàn trừ vào doanh thu gia sư (dạng phân số, 0.05 = 5%).', CURRENT_TIMESTAMP)
ON CONFLICT (config_key) DO NOTHING;

CREATE TABLE IF NOT EXISTS commission_config_history (
    history_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    parent_fee_percent NUMERIC(6,4) NOT NULL,
    tutor_fee_percent NUMERIC(6,4) NOT NULL,
    changed_by VARCHAR(50) REFERENCES users(user_id),
    changed_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_commission_config_history_changed_at
    ON commission_config_history (changed_at DESC);

INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('financial.manage','CMS / Tài chính','Cấu hình hoa hồng','Chỉnh sửa','Chỉnh sửa phí sàn thu của phụ huynh và gia sư')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label = EXCLUDED.label;

COMMIT;

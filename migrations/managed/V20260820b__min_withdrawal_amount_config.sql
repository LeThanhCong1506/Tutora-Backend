-- Ngưỡng rút tiền tối thiểu hiện đang hardcode trùng lặp ở WalletService.cs và
-- TutorFinanceService.cs (10000m). Chuyển sang system_configs để admin chỉnh trực tiếp từ CMS
-- (mục "Quy tắc thanh toán") mà không cần deploy lại backend.

BEGIN;

INSERT INTO system_configs (config_key, config_value, description, updated_at) VALUES
    ('min_withdrawal_amount', '10000', 'Số tiền tối thiểu (VND) gia sư/user phải đạt trước khi tạo yêu cầu rút tiền.', CURRENT_TIMESTAMP)
ON CONFLICT (config_key) DO NOTHING;

COMMIT;

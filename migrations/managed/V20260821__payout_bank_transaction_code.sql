-- Đối soát payout với sao kê ngân hàng.
--
-- provider_transaction_id đã có sẵn nhưng KHÔNG dùng được cho việc này: với payout thủ
-- công nó do PayoutCodeGenerator tự sinh (mã kiểm toán nội bộ), không phải số giao dịch
-- ngân hàng in trên biên lai. Vì vậy không có cách nào khớp một lệnh chi trong hệ thống
-- với đúng dòng trên sao kê ngân hàng — bằng chứng duy nhất là ảnh biên lai, phải mở ra
-- đọc bằng mắt.
--
-- Cột này lưu mã giao dịch DO NGÂN HÀNG cấp mà Admin/Staff nhập tay khi xác nhận đã
-- chuyển khoản (vd "FT26082212345678").
BEGIN;

ALTER TABLE public.payment_transactions
    ADD COLUMN IF NOT EXISTS bank_transaction_code varchar(100) NULL;

-- Không đặt UNIQUE: một mã trùng gần như chắc chắn là lỗi nhập liệu, nhưng chặn ở tầng DB
-- sẽ làm hỏng một lệnh chi thật lúc nửa đêm nếu ngân hàng có trường hợp ngoại lệ. Việc
-- chặn trùng nằm ở AdminPayoutService (báo lỗi rõ ràng cho staff); index này chỉ để tra
-- cứu khi đối soát cho nhanh.
CREATE INDEX IF NOT EXISTS idx_payment_transactions_bank_transaction_code
    ON public.payment_transactions (upper(bank_transaction_code))
    WHERE bank_transaction_code IS NOT NULL;

COMMENT ON COLUMN public.payment_transactions.bank_transaction_code IS
    'Mã giao dịch do ngân hàng cấp, Admin/Staff nhập tay khi xác nhận chuyển khoản payout. Dùng để đối soát với sao kê ngân hàng. Khác provider_transaction_id (mã nội bộ hệ thống tự sinh).';

COMMIT;

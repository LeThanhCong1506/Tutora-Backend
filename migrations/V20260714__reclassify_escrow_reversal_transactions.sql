-- Reclassify existing escrow "reversal" wallet transactions.
--
-- Bối cảnh: loại giao dịch `EscrowRelease` trước đây bị dùng lẫn cho 2 mục đích:
--   (1) Giải ngân THẬT vào số dư tutor khi buổi học hoàn tất  -> đúng là thu nhập.
--   (2) Hoàn/rút escrow khi hủy / tutor từ chối / tutor không phản hồi / no-show
--       -> tutor KHÔNG thực nhận (số tiền âm, hoặc dương nhưng không cộng vào Balance).
-- Việc gộp (2) vào tổng EscrowRelease làm totalEarned bị kéo xuống ÂM.
--
-- Từ nay (2) dùng loại mới `EscrowReversal`. Migration này gán lại các bản ghi CŨ:
--   - Giữ nguyên `EscrowRelease` cho giải ngân thật (mô tả "Giải ngân hoàn tất..." / "Kết thúc sớm...").
--   - Gán `EscrowReversal` cho phần còn lại: số tiền âm, hoặc mô tả bắt đầu bằng "Giải phóng escrow".
--
-- An toàn chạy lại nhiều lần (idempotent): chỉ tác động các dòng còn là 'EscrowRelease'.

BEGIN;

UPDATE public.wallet_transactions
SET transaction_type = 'EscrowReversal'
WHERE transaction_type = 'EscrowRelease'
  AND (
        amount < 0
        OR description LIKE 'Giải phóng escrow%'
      );

COMMIT;

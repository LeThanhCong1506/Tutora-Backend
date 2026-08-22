-- Buổi học bị ngắt giữa chừng (link phụ khi gia sư/học sinh có việc đột xuất, chỉ được kích
-- hoạt sau khi đã học đủ ngưỡng % tối thiểu) và buổi học lại do hai bên hoà giải một tranh chấp
-- (link 3, Admin/Staff mở). Cả 2 đều là 1 ClassSession row MỚI, tái dùng cột original_session_id
-- sẵn có (trước giờ chỉ dùng cho buổi bù no-show) để trỏ về buổi gốc; is_continuation/
-- is_dispute_relearn phân biệt lý do sinh ra row đó, không dùng chung cờ is_makeup vì luồng
-- no-show có logic hoàn tiền/giá riêng không áp dụng ở đây.
--
-- interrupted_by: user_id của người báo ngắt (gia sư/học sinh/phụ huynh) — lưu ID để tra cứu chính
-- xác (không lệch khi người dùng đổi tên), KHÔNG trả thẳng ra API/UI. Tầng response chỉ expose tên
-- đã resolve qua FK xuống users (xem InterruptedByName trên ClassSessionDetailResponse).

BEGIN;

ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS is_continuation BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS is_dispute_relearn BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS interrupted_at TIMESTAMP WITHOUT TIME ZONE;
ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS interrupt_reason TEXT;
ALTER TABLE class_sessions ADD COLUMN IF NOT EXISTS interrupted_by VARCHAR(50);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'class_sessions_interrupted_by_fkey'
    ) THEN
        ALTER TABLE class_sessions
            ADD CONSTRAINT class_sessions_interrupted_by_fkey
            FOREIGN KEY (interrupted_by) REFERENCES users (user_id);
    END IF;
END $$;

COMMIT;

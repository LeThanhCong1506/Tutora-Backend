-- Lý do admin ẩn một đánh giá, để gửi kèm trong thông báo cho người viết và gia sư,
-- đồng thời giữ dấu vết ai ẩn và ẩn lúc nào.
-- Ẩn/hiện là thao tác lặp lại được: khi hiện lại thì cả ba cột được xoá về NULL.

BEGIN;

ALTER TABLE public.feedbacks
    ADD COLUMN IF NOT EXISTS hidden_reason TEXT,
    ADD COLUMN IF NOT EXISTS hidden_at TIMESTAMP WITHOUT TIME ZONE,
    ADD COLUMN IF NOT EXISTS hidden_by VARCHAR(50);

COMMENT ON COLUMN public.feedbacks.hidden_reason IS 'Lý do admin ẩn đánh giá (vd vi phạm chính sách). NULL khi đang hiển thị.';
COMMENT ON COLUMN public.feedbacks.hidden_at IS 'Thời điểm bị ẩn. NULL khi đang hiển thị.';
COMMENT ON COLUMN public.feedbacks.hidden_by IS 'User id của admin/staff đã ẩn. NULL khi đang hiển thị.';

COMMIT;

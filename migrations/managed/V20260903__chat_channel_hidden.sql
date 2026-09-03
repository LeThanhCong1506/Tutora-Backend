-- Migration: Ẩn (xoá phía tôi) cuộc trò chuyện
-- Date: 2026-09-03
-- Purpose: FE cả 3 role (parent/student/tutor) có nút vuốt-để-xoá nhưng trước
--          đây chỉ hiện toast, kênh không mất — người dùng tưởng đã xoá.
--          - Xoá là SOFT-DELETE MỘT PHÍA: người kia vẫn thấy kênh và toàn bộ
--            tin nhắn. Không xoá chat_messages (cần cho khiếu nại/đối soát).
--          - Kênh HIỆN LẠI khi có tin nhắn mới sau thời điểm ẩn: so
--            chat_channels.last_message_at > hidden_at khi list. Nhờ vậy
--            không cần job dọn hay cập nhật ngược lúc gửi tin.
--          - Một kênh có tối đa 3 người (parent, tutor, student) nên trạng
--            thái ẩn phải nằm ở bảng riêng, không thể là cột trên kênh.

CREATE TABLE IF NOT EXISTS public.chat_channel_hidden (
    channel_id integer NOT NULL,
    user_id    character varying(50) NOT NULL,
    -- Mốc so với last_message_at để quyết định kênh có hiện lại hay không.
    hidden_at  timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT chat_channel_hidden_pkey PRIMARY KEY (channel_id, user_id),
    CONSTRAINT chat_channel_hidden_channel_fk
        FOREIGN KEY (channel_id) REFERENCES public.chat_channels (channel_id) ON DELETE CASCADE,
    CONSTRAINT chat_channel_hidden_user_fk
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE
);

-- List kênh của một user luôn lọc theo user_id: index cho nhánh đó.
CREATE INDEX IF NOT EXISTS idx_chat_channel_hidden_user
    ON public.chat_channel_hidden (user_id);

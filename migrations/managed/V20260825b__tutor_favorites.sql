-- Danh sách gia sư yêu thích, lưu theo từng tài khoản.
--
-- Trước đây wishlist chỉ nằm trong localStorage dưới MỘT key chung ('wishlistTutorIds'),
-- nên hai người đăng nhập cùng một máy sẽ thấy chung danh sách của nhau, và đăng nhập
-- máy khác thì trống trơn. Lưu xuống DB để wishlist đi theo tài khoản, đồng bộ giữa
-- web và Zalo Mini App, và không mất khi xoá cache trình duyệt.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tutor_favorites (
    favorite_id bigserial PRIMARY KEY,
    user_id     character varying(50) NOT NULL,
    tutor_id    character varying(50) NOT NULL,
    created_at  timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT tutor_favorites_user_fk
        FOREIGN KEY (user_id) REFERENCES public.users (user_id) ON DELETE CASCADE,
    -- Trỏ tới tutor_profiles (không phải users): gia sư bị xoá hồ sơ thì mục yêu thích
    -- cũng biến mất, không để lại dòng trỏ vào hư không.
    CONSTRAINT tutor_favorites_tutor_fk
        FOREIGN KEY (tutor_id) REFERENCES public.tutor_profiles (tutor_id) ON DELETE CASCADE,
    -- Một người chỉ lưu một gia sư một lần; bấm lại là bỏ lưu chứ không thêm dòng mới.
    CONSTRAINT tutor_favorites_unique UNIQUE (user_id, tutor_id)
);

-- Truy vấn chính là "wishlist của tôi, mới lưu lên đầu".
CREATE INDEX IF NOT EXISTS ix_tutor_favorites_user_created
    ON public.tutor_favorites (user_id, created_at DESC);

COMMENT ON TABLE public.tutor_favorites IS
    'Gia sư được một tài khoản lưu vào danh sách yêu thích. Thay cho localStorage dùng chung từ 2026-08-25.';

COMMIT;

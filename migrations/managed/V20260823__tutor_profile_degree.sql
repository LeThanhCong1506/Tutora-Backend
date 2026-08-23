-- Tách "học vị" ra khỏi ô học vấn của hồ sơ gia sư.
--
-- Trước đây chỉ có mỗi cột education — một chuỗi free-text nhét chung cả bằng cấp, chuyên
-- ngành lẫn tên trường ("Cử nhân Sư phạm Toán - Đại học Sư phạm Hà Nội"). FE (cả app gia sư
-- lẫn CMS) đã tách thành 2 ô riêng: "Học vị" chọn từ danh sách cố định (Cao đẳng / Cử nhân /
-- Kỹ sư / Thạc sĩ / Tiến sĩ / Phó Giáo sư / Giáo sư) và "Trường học" nhập tự do có gợi ý,
-- và đã gửi `degree` xuống API — nhưng BE chưa hề có chỗ nhận nên giá trị bị rơi im lặng.
--
-- 100 ký tự là dư sức cho danh sách học vị hiện tại; để rộng phòng khi thêm học vị mới
-- (vd "Nghiên cứu sinh Tiến sĩ") mà không phải đổi schema.
BEGIN;

ALTER TABLE public.tutor_profiles
    ADD COLUMN IF NOT EXISTS degree varchar(100) NULL;

COMMENT ON COLUMN public.tutor_profiles.degree IS
    'Học vị của gia sư (Cử nhân, Thạc sĩ, Tiến sĩ...). Tách khỏi education — education từ nay chỉ chứa TÊN TRƯỜNG. NULL với hồ sơ tạo trước 23/08/2026: dữ liệu cũ là chuỗi gộp không tách máy móc được nên cố ý KHÔNG backfill, gia sư tự chọn lại ở lần sửa hồ sơ kế tiếp.';

COMMIT;

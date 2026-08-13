-- Chuyển sang tự lưu file (LocalFileStorageService) thay vì Cloudinary — mất khả năng Cloudinary tự
-- render trang 1 PDF chứng chỉ thành ảnh xem trước qua URL transformation. Giờ backend tự render lúc
-- upload (PDFtoImage + SkiaSharp, đã có sẵn trong dự án) và lưu URL ảnh riêng ở đây.

BEGIN;

ALTER TABLE tutor_certificates
    ADD COLUMN IF NOT EXISTS thumbnail_url VARCHAR(2000);

COMMIT;

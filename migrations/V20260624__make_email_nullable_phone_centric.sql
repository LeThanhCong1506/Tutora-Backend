-- =====================================================
-- Migration: Make email nullable (phone-centric auth)
-- Date: 2026-06-24
-- Description:
--   - Auth chuyển sang tập trung số điện thoại: đăng ký bằng phone + verify OTP phone.
--     Email trở thành tùy chọn (Google vẫn có email thật; user đăng ký bằng phone có thể
--     không có email) nên bỏ ràng buộc NOT NULL trên cột email.
--   - phone vẫn nullable + UNIQUE (users_phone_key) như cũ — KHÔNG ép NOT NULL vì
--     tài khoản Google/Zalo và user cũ có thể chưa có số điện thoại.
-- =====================================================

BEGIN;

ALTER TABLE users ALTER COLUMN email DROP NOT NULL;

-- (TÙY CHỌN) Tránh khóa đăng nhập của user CŨ khi bật gate "phải verify phone":
-- mở comment dòng dưới nếu muốn cho các tài khoản hiện có (đã có phone) đăng nhập được ngay.
-- UPDATE users SET is_phone_verified = true WHERE is_phone_verified IS NOT TRUE AND phone IS NOT NULL;

COMMIT;

-- =====================================================
-- Verification (chạy sau migration) — is_nullable phải = YES
-- =====================================================
-- SELECT column_name, is_nullable
-- FROM information_schema.columns
-- WHERE table_name = 'users' AND column_name = 'email';

-- Tìm kiếm gia sư (searchTerm) hiện chỉ so khớp ILIKE trực tiếp trên chuỗi gốc, nên
-- gõ không dấu ("Cong Khong Ngu") không khớp được tên có dấu ("Công Không Ngủ").
-- Bật extension unaccent + tạo wrapper IMMUTABLE (unaccent() gốc không IMMUTABLE nên
-- không dùng trực tiếp được trong query/index) để tầng ứng dụng so khớp không dấu.

BEGIN;

CREATE EXTENSION IF NOT EXISTS unaccent;

CREATE OR REPLACE FUNCTION public.immutable_unaccent(text)
    RETURNS text
    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
    AS $_$
  SELECT public.unaccent($1);
$_$;

COMMIT;

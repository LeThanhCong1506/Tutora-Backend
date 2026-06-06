-- M4_T2_005: Normalize Studentid format
-- Migration date: 2026-05-22
-- Description:
--   Đồng nhất format Studentid về STU-{10 ký tự hex uppercase}.
--   Trước đây có nhiều cách generate khác nhau:
--     - STU-{parentId}-{seq}     (StudentService.CreateStudentAsync)
--     - Studentid = userId (Guid) (SimpleAuthService, RegisterService, ZaloAuthService)
--     - Format legacy seed data   (student-003, '1', 'USR-STU-01', ...)
--   Migration này map mọi Studentid không khớp format mới sang STU-{random} và
--   update tất cả FK ở các bảng tham chiếu.
--
-- Bảng bị ảnh hưởng:
--   - studentprofiles (PK studentid)
--   - bookings, lessons, studentgrades, learningmaterials, handoversummaries (FK ON DELETE CASCADE)
--   - chatchannels (cột studentid nullable, không có FK constraint)
--
-- Chạy trong transaction để rollback nếu fail.

BEGIN;

-- 1. Build mapping table: old_id -> new_id cho mọi Studentprofile có format không chuẩn
CREATE TEMP TABLE _studentid_map (
    old_id TEXT PRIMARY KEY,
    new_id TEXT NOT NULL UNIQUE
) ON COMMIT DROP;

INSERT INTO _studentid_map (old_id, new_id)
SELECT
    studentid,
    'STU-' || upper(substring(md5(random()::text || clock_timestamp()::text || studentid), 1, 10))
FROM studentprofiles
WHERE studentid !~ '^STU-[A-F0-9]{10}$';

-- 2. Hiển thị số lượng record sẽ được migrate (tham khảo trong log)
DO $$
DECLARE migrate_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO migrate_count FROM _studentid_map;
    RAISE NOTICE 'Migration sẽ đổi % studentid sang format mới', migrate_count;
END $$;

-- 3. Defer FK constraints để có thể update PK và FK trong cùng transaction
SET CONSTRAINTS
    bookings_studentid_fkey,
    lessons_studentid_fkey,
    studentgrades_studentid_fkey,
    learningmaterials_studentid_fkey,
    handoversummaries_studentid_fkey
DEFERRED;

-- 4. Update PK ở studentprofiles
UPDATE studentprofiles sp
SET studentid = m.new_id
FROM _studentid_map m
WHERE sp.studentid = m.old_id;

-- 5. Update FK ở các bảng tham chiếu
UPDATE bookings b
SET studentid = m.new_id
FROM _studentid_map m
WHERE b.studentid = m.old_id;

UPDATE lessons l
SET studentid = m.new_id
FROM _studentid_map m
WHERE l.studentid = m.old_id;

UPDATE studentgrades sg
SET studentid = m.new_id
FROM _studentid_map m
WHERE sg.studentid = m.old_id;

UPDATE learningmaterials lm
SET studentid = m.new_id
FROM _studentid_map m
WHERE lm.studentid = m.old_id;

UPDATE handoversummaries hs
SET studentid = m.new_id
FROM _studentid_map m
WHERE hs.studentid = m.old_id;

-- chatchannels.studentid là nullable, không có FK constraint, vẫn cập nhật để đồng bộ
UPDATE chatchannels cc
SET studentid = m.new_id
FROM _studentid_map m
WHERE cc.studentid = m.old_id;

-- 6. Verify: không còn FK orphan trong các bảng con
DO $$
DECLARE
    orphan_count INTEGER;
    invalid_format_count INTEGER;
BEGIN
    -- Check còn bản ghi nào Studentid không khớp format mới không
    SELECT COUNT(*) INTO invalid_format_count
    FROM studentprofiles
    WHERE studentid !~ '^STU-[A-F0-9]{10}$';

    IF invalid_format_count > 0 THEN
        RAISE EXCEPTION 'Còn % studentprofiles không khớp format STU-{10 hex} sau migration', invalid_format_count;
    END IF;

    -- Check FK orphan ở bookings
    SELECT COUNT(*) INTO orphan_count
    FROM bookings b
    LEFT JOIN studentprofiles sp ON b.studentid = sp.studentid
    WHERE b.studentid IS NOT NULL AND sp.studentid IS NULL;
    IF orphan_count > 0 THEN
        RAISE EXCEPTION 'Migration để lại % FK orphan trong bookings', orphan_count;
    END IF;

    -- Check FK orphan ở lessons
    SELECT COUNT(*) INTO orphan_count
    FROM lessons l
    LEFT JOIN studentprofiles sp ON l.studentid = sp.studentid
    WHERE l.studentid IS NOT NULL AND sp.studentid IS NULL;
    IF orphan_count > 0 THEN
        RAISE EXCEPTION 'Migration để lại % FK orphan trong lessons', orphan_count;
    END IF;

    RAISE NOTICE 'Migration hoàn tất, không phát hiện FK orphan';
END $$;

COMMIT;

-- Sửa liên kết nội bộ trong nội dung văn bản chính sách.
--
-- V20260812__policy_documents.sql seed link dạng /privacy, /terms — nhưng route thật của FE là
-- /policies/<slug> (route động theo slug để admin thêm văn bản mới qua CMS mà không cần deploy
-- lại FE). Không sửa trực tiếp file migration cũ vì ManagedMigrationRunner đối chiếu checksum và
-- sẽ ném "changed after it was applied" ở lần khởi động kế tiếp.
--
-- Idempotent: sau khi đổi, chuỗi trở thành '](/policies/terms)' nên không còn khớp '](/terms)'.

BEGIN;

UPDATE policy_documents
SET content_markdown = replace(
        replace(
            replace(
                replace(
                    replace(content_markdown, '](/terms)', '](/policies/terms)'),
                    '](/privacy)', '](/policies/privacy)'),
                '](/cookies)', '](/policies/cookies)'),
            '](/community-guidelines)', '](/policies/community-guidelines)'),
        '](/tutor-agreement)', '](/policies/tutor-agreement)')
WHERE content_markdown LIKE '%](/terms)%'
   OR content_markdown LIKE '%](/privacy)%'
   OR content_markdown LIKE '%](/cookies)%'
   OR content_markdown LIKE '%](/community-guidelines)%'
   OR content_markdown LIKE '%](/tutor-agreement)%';

COMMIT;

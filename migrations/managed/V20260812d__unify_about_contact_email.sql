-- The About page is CMS-backed, so update the published document at its source instead of
-- masking contact addresses in the frontend. Keep previous migrations immutable because the
-- managed migration runner validates their checksums.

UPDATE policy_documents
SET content_markdown = replace(
        content_markdown,
        E'- Hỗ trợ chung: **support@tutora.vn**\n- Hỗ trợ gia sư: **tutor-support@tutora.vn**\n- Dữ liệu cá nhân: **privacy@tutora.vn**\n- Báo cáo vi phạm: **trust-safety@tutora.vn**',
        E'- Email liên hệ: **tutoravn@gmail.com**'
    ),
    updated_at = CURRENT_TIMESTAMP
WHERE slug = 'about'
  AND content_markdown LIKE '%support@tutora.vn%';

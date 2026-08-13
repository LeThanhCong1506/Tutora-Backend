-- Use the single public contact address across every document shown under /about.
-- Keep the earlier managed migrations immutable because their checksums may already be journaled.

UPDATE policy_documents
SET content_markdown = replace(
        replace(
            replace(
                replace(content_markdown, 'tutor-support@tutora.vn', 'tutoravn@gmail.com'),
                'support@tutora.vn', 'tutoravn@gmail.com'
            ),
            'privacy@tutora.vn', 'tutoravn@gmail.com'
        ),
        'trust-safety@tutora.vn', 'tutoravn@gmail.com'
    ),
    updated_at = CURRENT_TIMESTAMP
WHERE slug IN ('terms', 'privacy', 'cookies', 'community-guidelines', 'tutor-agreement');

-- These values feed both the desktop/mobile sidebar and the H1 on each document page.
UPDATE policy_documents
SET title = CASE slug
        WHEN 'terms' THEN 'Điều khoản sử dụng dịch vụ'
        WHEN 'privacy' THEN 'Chính sách bảo mật'
        WHEN 'cookies' THEN 'Chính sách Cookie'
        WHEN 'community-guidelines' THEN 'Quy tắc cộng đồng'
        ELSE title
    END,
    updated_at = CURRENT_TIMESTAMP
WHERE slug IN ('terms', 'privacy', 'cookies', 'community-guidelines');

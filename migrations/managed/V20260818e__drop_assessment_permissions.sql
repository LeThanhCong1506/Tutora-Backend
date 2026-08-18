-- Bỏ quyền assessment.*: soạn đề là Admin-only, không đi qua hệ permission.
-- Xoá theo thứ tự FK trỏ vào permission_definitions.permission_key
-- (permission_definition_requirements có FK trên CẢ 2 cột).

BEGIN;

DELETE FROM public.permission_definition_requirements
WHERE permission_key LIKE 'assessment.%'
   OR required_permission_key LIKE 'assessment.%';

DELETE FROM public.permission_group_permissions
WHERE permission_key LIKE 'assessment.%';

DELETE FROM public.staff_permissions
WHERE permission_key LIKE 'assessment.%';

DELETE FROM public.permission_definitions
WHERE permission_key LIKE 'assessment.%';

COMMIT;

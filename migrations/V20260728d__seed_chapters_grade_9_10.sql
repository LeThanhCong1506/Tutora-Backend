-- Migration: bổ sung đủ chương SGK Toán lớp 9 và lớp 10
-- Date: 2026-07-28
-- Purpose: Bảng chapters trước đây được sinh RA TỪ 902 câu hỏi đã nhập ngân hàng đề
--          (đều ngày 2026-07-09), nên nó phản ánh "chương nào đã có đề" chứ không phải
--          "chương nào có trong chương trình học". Hệ quả: lớp 9 chỉ có 3 chương trong khi
--          lớp 11 có 12, lớp 12 có 11 — vì ngân hàng đề tập trung luyện thi THPT.
--
-- Lớp 9: grade_level_id = 57, môn Toán subject_id = 1
INSERT INTO public.chapters (subject_id, grade_level_id, slug, name, display_order)
VALUES
    (1, 57, 'can_bac_hai',                                    'Căn bậc hai và căn bậc ba',              2),
    (1, 57, 'phuong_trinh_va_he_phuong_trinh_bac_nhat_hai_an','Phương trình và hệ phương trình bậc nhất hai ẩn', 1),
    (1, 57, 'bat_phuong_trinh_bac_nhat_mot_an',               'Bất đẳng thức và bất phương trình bậc nhất một ẩn', 3),
    (1, 57, 'he_thuc_luong_trong_tam_giac_vuong',             'Hệ thức lượng trong tam giác vuông',     4),
    (1, 57, 'duong_tron',                                     'Đường tròn',                             5),
    (1, 57, 'phuong_trinh_bac_hai_mot_an',                    'Hàm số y = ax² và phương trình bậc hai một ẩn', 6),
    (1, 57, 'da_thuc',                                        'Đa thức',                                7),
    (1, 57, 'tu_giac_noi_tiep_da_giac_deu',                   'Đường tròn ngoại tiếp và nội tiếp',      8),
    (1, 57, 'hinh_tru_hinh_non_hinh_cau',                     'Hình trụ, hình nón, hình cầu',           9),
    (1, 57, 'tan_so_tan_so_tuong_doi',                        'Tần số và tần số tương đối',            10),
    (1, 57, 'xac_suat_cua_bien_co',                           'Xác suất của biến cố',                  11)
ON CONFLICT (subject_id, grade_level_id, slug) DO UPDATE
    SET name = EXCLUDED.name,
        display_order = EXCLUDED.display_order,
        is_active = true,
        updated_at = now();

-- Lớp 10: grade_level_id = 58
INSERT INTO public.chapters (subject_id, grade_level_id, slug, name, display_order)
VALUES
    (1, 58, 'menh_de_tap_hop',                   'Mệnh đề và tập hợp',                        1),
    (1, 58, 'menh_de',                           'Mệnh đề',                                   2),
    (1, 58, 'bat_phuong_trinh_bac_nhat_hai_an',  'Bất phương trình bậc nhất hai ẩn',          3),
    (1, 58, 'he_thuc_luong_trong_tam_giac',      'Hệ thức lượng trong tam giác',              4),
    (1, 58, 'gia_tri_luong_giac',                'Giá trị lượng giác của một góc',            5),
    (1, 58, 'vecto',                             'Vectơ',                                     6),
    (1, 58, 'ham_so_va_do_thi',                  'Hàm số, đồ thị và ứng dụng',                7),
    (1, 58, 'ham_so_bac_hai_va_do_thi',          'Hàm số bậc hai và đồ thị',                  8),
    (1, 58, 'phuong_phap_toa_do_trong_mat_phang','Phương pháp toạ độ trong mặt phẳng',        9),
    (1, 58, 'dai_so_to_hop',                     'Đại số tổ hợp',                            10),
    (1, 58, 'cac_so_dac_trung_cua_mau_so_lieu',  'Các số đặc trưng của mẫu số liệu không ghép nhóm', 11),
    (1, 58, 'xac_suat_bien_co',                  'Xác suất của biến cố',                     12)
ON CONFLICT (subject_id, grade_level_id, slug) DO UPDATE
    SET name = EXCLUDED.name,
        display_order = EXCLUDED.display_order,
        is_active = true,
        updated_at = now();

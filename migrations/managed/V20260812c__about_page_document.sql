-- Gộp phần giới thiệu và các văn bản pháp lý về một trang duy nhất (/about) với sidebar.
--
-- Phần "Giới thiệu Tutora" được lưu như một policy_document bình thường (slug `about`,
-- display_order 0 nên đứng đầu sidebar và là trang mặc định). Làm vậy để admin sửa được
-- nội dung giới thiệu bằng đúng màn CMS đang dùng cho điều khoản, thay vì phải hard-code
-- một trang riêng trong FE rồi mỗi lần đổi chữ lại phải deploy.
--
-- Kèm theo: đổi liên kết nội bộ /policies/<slug> → /about/<slug> cho khớp route mới.
-- Không sửa hai file migration trước vì ManagedMigrationRunner đối chiếu checksum.

BEGIN;

INSERT INTO policy_documents (slug, title, summary, version, effective_date, status, display_order, published_at, content_markdown)
VALUES (
    'about',
    'Về Tutora',
    'Tutora là nền tảng kết nối phụ huynh, học viên với gia sư đã được xác minh, kèm công cụ dạy học trực tuyến và theo dõi tiến độ.',
    '1.0',
    DATE '2026-08-12',
    'published',
    0,
    CURRENT_TIMESTAMP,
$md$Tutora là nền tảng kết nối trực tuyến giữa **phụ huynh, học viên** và **gia sư** cho bậc K-12, hoạt động trên website và Zalo Mini App.

Chúng tôi không trực tiếp giảng dạy. Việc của Tutora là làm cho việc học kèm trực tuyến trở nên đáng tin: kiểm duyệt hồ sơ gia sư trước khi cho nhận lớp, giữ tiền học phí cho tới khi buổi học thực sự diễn ra, và ghi lại đủ bằng chứng để phân xử khi hai bên không đồng ý với nhau.

## Nền tảng làm được gì

- **Tìm gia sư phù hợp** — lọc theo môn, khối lớp, khu vực và ngân sách; mỗi hồ sơ đều đã qua kiểm duyệt.
- **Lớp học trực tuyến** — video call kèm bảng trắng tương tác, không cần cài thêm phần mềm.
- **Theo dõi tiến độ từng buổi** — sau mỗi buổi gia sư gửi báo cáo nội dung đã dạy và bài tập; phụ huynh xem lại được cả lịch sử.
- **Trợ lý học tập AI** — hỗ trợ giải đáp ngoài giờ học, hoạt động theo cơ chế tín dụng.
- **Ví Tutora** — nạp, thanh toán và rút tiền trong một chỗ, có đối soát rõ ràng.

## Cách chúng tôi giữ an toàn cho học viên

Gia sư phải hoàn tất xác minh danh tính bằng giấy tờ tùy thân trước khi hồ sơ được hiển thị. Buổi học có thể được ghi hình, và hệ thống lưu nhật ký hiện diện của cả hai phía — đây là căn cứ khi có khiếu nại về việc dạy không đúng cam kết hoặc vắng mặt.

Học viên dưới 18 tuổi tham gia qua tài khoản do phụ huynh tạo và quản lý. Chi tiết về dữ liệu chúng tôi thu thập và cách bảo vệ, xem [Chính sách bảo mật](/about/privacy).

## Tiền của bạn được giữ thế nào

Học phí không chuyển thẳng cho gia sư. Tiền nằm ở tài khoản tạm giữ của nền tảng và chỉ được giải ngân theo từng buổi đã hoàn tất. Buổi nào đang có khiếu nại thì phần tiền của buổi đó giữ nguyên cho tới khi quản trị viên ra quyết định.

## Văn bản pháp lý

Toàn bộ điều khoản, chính sách và quy tắc áp dụng cho các bên có trong danh sách bên trái. Nếu bạn là gia sư, hãy đọc kỹ [Thỏa thuận hợp tác dành cho Gia sư](/about/tutor-agreement) — văn bản này quy định riêng về hoa hồng, nghĩa vụ giảng dạy và cách xử lý vi phạm.

## Liên hệ

- Hỗ trợ chung: **support@tutora.vn**
- Hỗ trợ gia sư: **tutor-support@tutora.vn**
- Dữ liệu cá nhân: **privacy@tutora.vn**
- Báo cáo vi phạm: **trust-safety@tutora.vn**$md$
)
ON CONFLICT (slug) DO NOTHING;

-- Đổi liên kết nội bộ sang prefix mới. Idempotent: sau khi đổi, '/about/terms' không còn
-- khớp '/policies/terms' nữa.
UPDATE policy_documents
SET content_markdown = replace(content_markdown, '](/policies/', '](/about/')
WHERE content_markdown LIKE '%](/policies/%';

COMMIT;

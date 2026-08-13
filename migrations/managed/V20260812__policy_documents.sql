-- Văn bản pháp lý (điều khoản, bảo mật, cookie, quy tắc cộng đồng, thoả thuận gia sư) chuyển từ
-- hard-code trong FE sang DB để admin tự CRUD qua CMS.
--
-- Nội dung lưu dạng Markdown: FE render bằng react-markdown + remark-gfm (đã có sẵn trong
-- Tutora-FE), và Markdown là thứ người soạn thảo pháp lý sửa được mà không cần biết HTML.
--
-- Tiêu đề H1 và dòng "Cập nhật lần cuối" của bản .md gốc KHÔNG nằm trong content: chúng đã là
-- cột title và effective_date, để trong content nữa sẽ hiển thị lặp hai lần trên trang.
--
-- Seed dùng ON CONFLICT (slug) DO NOTHING — chạy lại migration không được ghi đè bản mà admin
-- đã chỉnh sửa qua CMS.

BEGIN;

CREATE TABLE IF NOT EXISTS policy_documents (
    policy_document_id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    slug               VARCHAR(80)  NOT NULL UNIQUE,
    title              VARCHAR(200) NOT NULL,
    summary            VARCHAR(500),
    content_markdown   TEXT         NOT NULL,
    version            VARCHAR(20)  NOT NULL DEFAULT '1.0',
    effective_date     DATE,
    -- archived = xoá mềm. Không xoá cứng vì đây là văn bản người dùng đã bấm đồng ý;
    -- cần giữ lại để đối chiếu khi có tranh chấp về việc "lúc đó điều khoản nói gì".
    status             VARCHAR(20)  NOT NULL DEFAULT 'draft',
    display_order      INTEGER      NOT NULL DEFAULT 0,
    published_at       TIMESTAMP WITHOUT TIME ZONE,
    created_at         TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by         VARCHAR(50) REFERENCES users(user_id) ON DELETE SET NULL,
    CONSTRAINT policy_documents_status_check CHECK (status IN ('draft', 'published', 'archived'))
);

-- Trang công khai luôn lọc status='published' rồi sắp theo display_order.
CREATE INDEX IF NOT EXISTS idx_policy_documents_status_order
    ON policy_documents (status, display_order);

-- ── Quyền cho CMS ──
INSERT INTO permission_definitions(permission_key, domain, module, action, label) VALUES
('policy.view','CMS / Nội dung','Chính sách','Xem','Xem văn bản chính sách'),
('policy.manage','CMS / Nội dung','Chính sách','Quản lý','Tạo, sửa, xuất bản văn bản chính sách')
ON CONFLICT (permission_key) DO UPDATE SET
    domain = EXCLUDED.domain,
    module = EXCLUDED.module,
    action = EXCLUDED.action,
    label  = EXCLUDED.label;

INSERT INTO permission_definition_requirements(permission_key, required_permission_key) VALUES
('policy.manage','policy.view')
ON CONFLICT DO NOTHING;

-- ── Seed 5 văn bản hiện hành ──
-- Liên kết nội bộ đã đổi sang route thật (/terms, /privacy). Hai văn bản được nhắc tới nhưng
-- chưa tồn tại — "Chính sách An toàn Trẻ em" và "Chính sách hoàn tiền & hủy buổi học" — để
-- nguyên dạng chữ thường, không bọc link, tránh dẫn người dùng tới trang 404.

INSERT INTO policy_documents (slug, title, summary, version, effective_date, status, display_order, published_at, content_markdown)
VALUES (
    'terms',
    'Điều khoản sử dụng dịch vụ Tutora',
    'Các điều kiện khi bạn đăng ký và sử dụng Tutora, dù với vai trò Học viên, Phụ huynh hay Gia sư.',
    '1.0',
    DATE '2026-08-07',
    'published',
    1,
    CURRENT_TIMESTAMP,
$md$## 1. Giới thiệu

Tutora ("chúng tôi", "nền tảng") là nền tảng kết nối trực tuyến giữa **Học viên/Phụ huynh** và **Gia sư**, cung cấp dịch vụ dạy học trực tuyến qua video call, bảng trắng tương tác, nhắn tin, trợ lý AI và các công cụ hỗ trợ học tập liên quan, hoạt động trên website và Zalo Mini App.

Bằng việc đăng ký, truy cập hoặc sử dụng Tutora dưới bất kỳ hình thức nào, bạn đồng ý bị ràng buộc bởi Điều khoản sử dụng này, [Chính sách bảo mật](/privacy) và Chính sách hoàn tiền & hủy buổi học. Nếu không đồng ý, vui lòng ngừng sử dụng dịch vụ.

## 2. Định nghĩa

- **Người dùng**: bất kỳ cá nhân nào tạo tài khoản trên Tutora, bao gồm Học viên, Phụ huynh, Gia sư.
- **Học viên**: người sử dụng dịch vụ để học.
- **Phụ huynh**: người tạo và quản lý tài khoản cho con chưa đủ 18 tuổi (tài khoản liên kết cha mẹ - con).
- **Gia sư**: cá nhân đăng ký cung cấp dịch vụ dạy học qua nền tảng, sau khi hoàn tất quy trình xác minh.
- **Buổi học/Lớp học**: một phiên dạy - học được đặt lịch và diễn ra qua nền tảng.
- **Ví Tutora**: tài khoản số dư nội bộ dùng để nạp tiền, thanh toán buổi học, nhận thanh toán và rút tiền.

## 3. Điều kiện sử dụng và tài khoản

3.1. Người dùng phải từ đủ 18 tuổi để tự đăng ký tài khoản. Người dùng dưới 18 tuổi chỉ được sử dụng dịch vụ thông qua tài khoản do Phụ huynh/người giám hộ hợp pháp tạo và chịu trách nhiệm quản lý.

3.2. Bạn cam kết cung cấp thông tin đăng ký chính xác, đầy đủ và cập nhật (họ tên, email, số điện thoại, ngày sinh...). Tutora có quyền tạm khóa hoặc chấm dứt tài khoản cung cấp thông tin sai lệch.

3.3. Bạn có thể đăng ký/đăng nhập qua email-mật khẩu, số điện thoại, Google hoặc Zalo. Bạn chịu trách nhiệm bảo mật thông tin đăng nhập và mọi hoạt động phát sinh từ tài khoản của mình.

3.4. Mỗi cá nhân chỉ được sở hữu một tài khoản Học viên/Gia sư, trừ tài khoản Phụ huynh quản lý nhiều hồ sơ con.

## 4. Đăng ký và xác minh Gia sư

4.1. Ứng viên Gia sư phải cung cấp thông tin trình độ học vấn, kinh nghiệm giảng dạy, môn học, học phí đề xuất, video giới thiệu, chứng chỉ liên quan.

4.2. Gia sư phải hoàn tất **xác minh danh tính (eKYC)**, bao gồm cung cấp số Căn cước công dân/CMND và ảnh chụp hai mặt giấy tờ tùy thân, để đảm bảo an toàn cho Học viên. Tài khoản Gia sư trải qua các trạng thái: *Nháp → Chờ duyệt → Hoạt động/Bị từ chối*.

4.3. Tutora có quyền từ chối, tạm ngưng hoặc chấm dứt tư cách Gia sư nếu phát hiện thông tin giả mạo, vi phạm điều khoản, hoặc nhận phản ánh vi phạm từ Học viên.

4.4. Gia sư là đối tác độc lập, **không phải người lao động của Tutora**. Tutora không chịu trách nhiệm về chất lượng giảng dạy cụ thể của từng Gia sư ngoài phạm vi cam kết tại nền tảng, nhưng có cơ chế đánh giá, khiếu nại và xử lý vi phạm.

## 5. Đặt lịch, thanh toán và Ví Tutora

5.1. Học viên đặt buổi học theo lịch trống của Gia sư. Học phí được hiển thị công khai trước khi đặt lịch.

5.2. Thanh toán được thực hiện qua **Ví Tutora**: Học viên nạp tiền vào ví qua cổng thanh toán **PayOS**; số tiền học phí được giữ tạm (escrow) cho đến khi buổi học hoàn thành, sau đó chuyển vào ví Gia sư trừ phí dịch vụ nền tảng.

5.3. Gia sư có thể yêu cầu **rút tiền** từ số dư ví về tài khoản ngân hàng đã đăng ký. Thời gian xử lý và điều kiện rút tiền tối thiểu được quy định riêng trong mục Ví trên nền tảng.

5.4. Phí dịch vụ, tỷ lệ chiết khấu áp dụng cho Gia sư được công bố minh bạch và có thể thay đổi; Tutora sẽ thông báo trước khi thay đổi có hiệu lực.

5.5. Chính sách hủy lịch, hoàn tiền, xử lý tranh chấp buổi học được quy định chi tiết tại Chính sách hoàn tiền & hủy buổi học.

## 6. Buổi học trực tuyến

6.1. Buổi học diễn ra qua video call tích hợp (Agora) kèm bảng trắng tương tác. Buổi học **có thể được ghi âm/ghi hình** phục vụ mục đích đảm bảo chất lượng, giải quyết tranh chấp và lưu trữ theo yêu cầu của người dùng; việc ghi hình được thông báo cho các bên tham gia.

6.2. Người dùng không được ghi lại, phát tán, khai thác thương mại nội dung buổi học nếu chưa được sự đồng ý của các bên liên quan và Tutora.

6.3. Người dùng không được trao đổi thông tin liên hệ cá nhân hoặc thu xếp thanh toán/giao dịch ngoài nền tảng nhằm né tránh phí dịch vụ ("giao dịch chui"). Hành vi này có thể dẫn đến khóa tài khoản vĩnh viễn.

## 7. Trợ lý AI

Tutora cung cấp trợ lý học tập AI (sử dụng công nghệ của bên thứ ba) hoạt động theo cơ chế tín dụng (credit). Nội dung do AI tạo ra chỉ mang tính tham khảo, không thay thế hoàn toàn việc giảng dạy của Gia sư; Tutora không đảm bảo tính chính xác tuyệt đối của nội dung AI sinh ra.

## 8. Quy tắc ứng xử

Người dùng không được:
- Cung cấp thông tin giả mạo, mạo danh người khác;
- Có hành vi quấy rối, phân biệt đối xử, ngôn từ thù ghét, xâm hại tình dục, bạo lực đối với người dùng khác (đặc biệt là trẻ vị thành niên);
- Đăng tải nội dung vi phạm pháp luật Việt Nam hoặc quyền sở hữu trí tuệ;
- Gian lận thanh toán, tạo tài khoản ảo, đánh giá giả;
- Cố ý phá hoại, xâm nhập trái phép hệ thống Tutora.

Vi phạm có thể dẫn đến cảnh cáo, tạm khóa hoặc chấm dứt tài khoản mà không hoàn lại số dư ví trong trường hợp gian lận.

## 9. Đánh giá và phản hồi

Sau mỗi buổi học, Học viên/Gia sư có thể đánh giá, để lại nhận xét. Đánh giá phải trung thực, không xúc phạm; Tutora có quyền gỡ bỏ đánh giá vi phạm quy định.

## 10. Sở hữu trí tuệ

Toàn bộ thương hiệu, giao diện, mã nguồn, nội dung do Tutora phát triển thuộc quyền sở hữu của Tutora. Nội dung do Gia sư tạo ra (giáo trình, tài liệu) thuộc quyền sở hữu của Gia sư, nhưng Gia sư cấp cho Tutora quyền sử dụng phi độc quyền để vận hành và quảng bá dịch vụ.

## 11. Giới hạn trách nhiệm

Tutora là nền tảng trung gian kết nối, không trực tiếp cung cấp dịch vụ giảng dạy. Trong phạm vi pháp luật cho phép, Tutora không chịu trách nhiệm đối với thiệt hại gián tiếp phát sinh từ chất lượng giảng dạy, sự cố kỹ thuật ngoài tầm kiểm soát (mất kết nối, lỗi bên thứ ba), hoặc hành vi của người dùng khác.

## 12. Chấm dứt dịch vụ

Người dùng có quyền yêu cầu xóa/khóa tài khoản bất kỳ lúc nào. Tutora có quyền tạm ngưng hoặc chấm dứt tài khoản vi phạm nghiêm trọng điều khoản này. Số dư ví hợp lệ còn lại sẽ được hoàn trả theo quy trình rút tiền, trừ trường hợp gian lận.

## 13. Thay đổi điều khoản

Tutora có thể cập nhật Điều khoản sử dụng theo thời gian. Thay đổi quan trọng sẽ được thông báo qua email hoặc trong ứng dụng trước khi có hiệu lực. Việc tiếp tục sử dụng dịch vụ sau khi thay đổi có hiệu lực đồng nghĩa với việc bạn chấp nhận điều khoản mới.

## 14. Luật áp dụng và giải quyết tranh chấp

Điều khoản này được điều chỉnh bởi pháp luật Việt Nam. Mọi tranh chấp phát sinh sẽ được ưu tiên giải quyết thông qua thương lượng; nếu không thành, sẽ được đưa ra Tòa án có thẩm quyền tại Việt Nam.

## 15. Liên hệ

Mọi thắc mắc về Điều khoản sử dụng, vui lòng liên hệ: **support@tutora.vn**$md$
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO policy_documents (slug, title, summary, version, effective_date, status, display_order, published_at, content_markdown)
VALUES (
    'privacy',
    'Chính sách bảo mật Tutora',
    'Chúng tôi thu thập dữ liệu gì, dùng để làm gì, chia sẻ với ai, và bạn kiểm soát dữ liệu của mình ra sao.',
    '1.0',
    DATE '2026-08-07',
    'published',
    2,
    CURRENT_TIMESTAMP,
$md$Chính sách này áp dụng cho website, ứng dụng và Zalo Mini App Tutora, được xây dựng phù hợp với **Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân** và pháp luật Việt Nam hiện hành.

## 1. Dữ liệu cá nhân chúng tôi thu thập

**a) Thông tin do bạn cung cấp khi đăng ký/sử dụng dịch vụ**
- Họ tên, email, số điện thoại, ngày sinh, giới tính, ảnh đại diện, địa chỉ (tỉnh/thành, quận/huyện);
- Đối với tài khoản Phụ huynh: thông tin liên kết với hồ sơ con (họ tên, ngày sinh của trẻ);
- Đối với Gia sư: trình độ học vấn, GPA, kinh nghiệm giảng dạy, môn học, học phí, video giới thiệu, chứng chỉ tải lên;
- **Dữ liệu định danh (eKYC)**: số Căn cước công dân/CMND, ảnh chụp mặt trước/sau giấy tờ tùy thân, dùng để xác minh danh tính Gia sư nhằm đảm bảo an toàn nền tảng;
- **Thông tin tài khoản ngân hàng** (tên chủ tài khoản, số tài khoản) phục vụ việc rút tiền của Gia sư;
- Nội dung tin nhắn, đánh giá, phản hồi trao đổi qua nền tảng.

**b) Thông tin thu thập tự động**
- Dữ liệu đăng nhập, lịch sử truy cập, thiết bị, địa chỉ IP, mã token thiết bị (push notification);
- Dữ liệu phiên học: lịch sử đặt lịch, **bản ghi âm/ghi hình buổi học video (Agora)**, nội dung bảng trắng tương tác;
- Dữ liệu giao dịch: lịch sử nạp/rút Ví Tutora, thanh toán qua PayOS.

**c) Thông tin từ bên thứ ba**
- Khi đăng nhập bằng Google hoặc Zalo: tên, email/số điện thoại, ảnh đại diện theo phạm vi bạn cấp quyền;
- Nếu bạn liên kết Google Calendar: quyền truy cập lịch để đồng bộ lịch dạy - học (chỉ trong phạm vi được cấp quyền).

## 2. Mục đích sử dụng dữ liệu

Chúng tôi sử dụng dữ liệu cá nhân để:
1. Tạo, xác thực và quản lý tài khoản;
2. Xác minh danh tính Gia sư (eKYC) nhằm đảm bảo an toàn cho Học viên, đặc biệt là trẻ vị thành niên;
3. Kết nối, đặt lịch, tổ chức và ghi nhận buổi học;
4. Xử lý thanh toán, quản lý Ví Tutora, đối soát và chuyển tiền cho Gia sư qua PayOS/ngân hàng;
5. Cung cấp trợ lý học tập AI theo yêu cầu người dùng;
6. Gửi thông báo dịch vụ, nhắc lịch, khuyến mãi (có thể từ chối nhận);
7. Giải quyết khiếu nại, tranh chấp, phòng chống gian lận;
8. Cải thiện chất lượng dịch vụ, phân tích, thống kê (dữ liệu ẩn danh khi có thể);
9. Tuân thủ nghĩa vụ pháp lý theo yêu cầu cơ quan nhà nước có thẩm quyền.

## 3. Cơ sở pháp lý xử lý dữ liệu

Chúng tôi xử lý dữ liệu dựa trên: (i) sự đồng ý của bạn khi đăng ký/sử dụng dịch vụ; (ii) nhu cầu thực hiện hợp đồng cung cấp dịch vụ; (iii) nghĩa vụ tuân thủ pháp luật (phòng chống rửa tiền, xác minh danh tính); (iv) lợi ích hợp pháp của Tutora trong việc đảm bảo an toàn nền tảng.

Đối với dữ liệu của trẻ vị thành niên, việc thu thập, xử lý luôn dựa trên sự đồng ý và giám sát của Phụ huynh/người giám hộ.

## 4. Chia sẻ dữ liệu với bên thứ ba

Chúng tôi chia sẻ dữ liệu cá nhân trong phạm vi cần thiết với các đối tác sau để vận hành dịch vụ:

| Đối tác | Mục đích | Dữ liệu liên quan |
|---|---|---|
| PayOS | Xử lý thanh toán, nạp/rút ví | Thông tin thanh toán, số tài khoản |
| Agora | Video call, ghi hình buổi học | Âm thanh, hình ảnh buổi học |
| Netless (Fastboard) | Bảng trắng tương tác | Nội dung bảng trắng |
| Google (OAuth, Calendar) | Đăng nhập, đồng bộ lịch | Email, lịch trình |
| Zalo | Đăng nhập, Zalo Mini App | Tên, số điện thoại (theo quyền cấp) |
| Supabase | Xác thực, quản lý phiên đăng nhập | Thông tin tài khoản |
| Resend | Gửi email thông báo/xác thực | Email |
| Cloudinary, AWS S3, Google Drive | Lưu trữ ảnh, tài liệu, bản ghi buổi học | Ảnh, video, tài liệu |
| Google Gemini (AI) | Trợ lý học tập AI | Nội dung câu hỏi/hội thoại với AI |
| Cơ quan nhà nước có thẩm quyền | Tuân thủ pháp luật khi có yêu cầu hợp lệ | Theo yêu cầu cụ thể |

Chúng tôi **không bán** dữ liệu cá nhân của người dùng cho bên thứ ba vì mục đích thương mại.

## 5. Lưu trữ và bảo mật dữ liệu

- Dữ liệu được lưu trữ trên hạ tầng có mã hóa truyền tải (HTTPS/TLS) và các biện pháp kiểm soát truy cập phù hợp.
- **Ảnh giấy tờ tùy thân** (mặt trước/sau CCCD/CMND) được lưu trữ tách biệt trong kho lưu trữ riêng, chỉ truy cập được qua đường dẫn có chữ ký (signed URL) do hệ thống cấp cho nhân sự có thẩm quyền phục vụ xác minh.
- **Số CCCD/CMND** được lưu trữ như một trường thông tin hồ sơ thông thường cùng với các thông tin cá nhân khác của tài khoản, được bảo vệ bằng các biện pháp kiểm soát truy cập chung áp dụng cho toàn bộ dữ liệu người dùng (không phải cơ chế cách ly/kiểm soát truy cập riêng như ảnh giấy tờ). Chúng tôi đang tiếp tục đánh giá và có thể nâng cấp mức bảo vệ cho trường dữ liệu này trong tương lai.
- Bản ghi buổi học được lưu trữ trong thời gian hợp lý phục vụ giải quyết tranh chấp, sau đó có thể bị xóa hoặc theo yêu cầu người dùng.
- Chúng tôi lưu giữ dữ liệu trong thời gian cần thiết để đạt được mục đích thu thập hoặc theo yêu cầu pháp luật; sau đó dữ liệu sẽ được xóa hoặc ẩn danh hóa.

## 6. Quyền của chủ thể dữ liệu

Theo Nghị định 13/2023/NĐ-CP, bạn có quyền:
- Được biết về việc xử lý dữ liệu cá nhân của mình;
- Đồng ý hoặc rút lại sự đồng ý xử lý dữ liệu;
- Truy cập, chỉnh sửa, yêu cầu xóa dữ liệu cá nhân (trong phạm vi pháp luật cho phép và nghĩa vụ hợp đồng đang thực hiện);
- Hạn chế hoặc phản đối việc xử lý dữ liệu;
- Yêu cầu cung cấp bản sao dữ liệu cá nhân;
- Khiếu nại, tố cáo, khởi kiện theo quy định pháp luật;
- Yêu cầu bồi thường thiệt hại nếu có vi phạm.

Để thực hiện các quyền trên, vui lòng liên hệ **privacy@tutora.vn**. Chúng tôi phản hồi trong thời hạn theo quy định pháp luật hiện hành.

## 7. Dữ liệu trẻ vị thành niên

Tutora coi trọng việc bảo vệ dữ liệu trẻ em. Tài khoản của người dùng dưới 18 tuổi chỉ được tạo và quản lý bởi Phụ huynh/người giám hộ hợp pháp. Chúng tôi không chủ động thu thập dữ liệu từ trẻ em ngoài phạm vi Phụ huynh cung cấp và cho phép, đồng thời giới hạn hiển thị thông tin liên hệ của trẻ với Gia sư ở mức cần thiết để tổ chức buổi học.

## 8. Cookie và công nghệ theo dõi

Website Tutora sử dụng cookie/local storage để duy trì phiên đăng nhập, ghi nhớ tùy chọn và phân tích sử dụng. Bạn có thể quản lý cookie qua cài đặt trình duyệt; việc tắt một số cookie có thể ảnh hưởng đến trải nghiệm sử dụng dịch vụ. Chi tiết xem [Chính sách Cookie](/cookies).

## 9. Chuyển dữ liệu ra nước ngoài

Một số nhà cung cấp dịch vụ của chúng tôi (Google, AWS, Agora, Netless) có máy chủ đặt tại nước ngoài. Việc chuyển dữ liệu ra nước ngoài (nếu có) được thực hiện phù hợp với quy định của Nghị định 13/2023/NĐ-CP về chuyển dữ liệu cá nhân xuyên biên giới.

## 10. Thay đổi chính sách

Chính sách bảo mật có thể được cập nhật theo thời gian. Thay đổi quan trọng sẽ được thông báo qua email hoặc trong ứng dụng.

## 11. Liên hệ

Nếu có câu hỏi về Chính sách bảo mật hoặc muốn thực hiện quyền của chủ thể dữ liệu, vui lòng liên hệ:

**Email**: privacy@tutora.vn$md$
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO policy_documents (slug, title, summary, version, effective_date, status, display_order, published_at, content_markdown)
VALUES (
    'cookies',
    'Chính sách Cookie Tutora',
    'Cách Tutora sử dụng cookie và các công nghệ lưu trữ tương tự trên website và ứng dụng.',
    '1.0',
    DATE '2026-08-07',
    'published',
    3,
    CURRENT_TIMESTAMP,
$md$Chính sách này giải thích cách Tutora sử dụng cookie và các công nghệ lưu trữ tương tự (local storage, session storage) trên website và ứng dụng.

## 1. Cookie là gì?

Cookie là tệp dữ liệu nhỏ được lưu trên trình duyệt/thiết bị của bạn khi truy cập website, giúp nền tảng ghi nhớ thông tin phiên làm việc và tùy chọn của bạn.

## 2. Các loại cookie Tutora sử dụng

| Loại | Mục đích | Có thể tắt? |
|---|---|---|
| **Cookie cần thiết** | Duy trì phiên đăng nhập, xác thực (Supabase), bảo mật chống giả mạo yêu cầu | Không (bắt buộc để dùng dịch vụ) |
| **Cookie chức năng** | Ghi nhớ tùy chọn ngôn ngữ, giao diện, lịch sử tìm kiếm gia sư | Có |
| **Cookie phân tích** | Thống kê lượt truy cập, hành vi sử dụng để cải thiện sản phẩm | Có |
| **Cookie bên thứ ba** | Nhúng từ Google (đăng nhập, Calendar), Agora (video call), Zalo Mini App | Phụ thuộc dịch vụ liên kết |

## 3. Quản lý cookie

Bạn có thể quản lý hoặc xóa cookie thông qua cài đặt trình duyệt (Chrome, Safari, Edge, Cờ-rôm trên di động...). Việc tắt cookie cần thiết có thể khiến bạn không đăng nhập được hoặc một số tính năng không hoạt động đúng.

Đối với Zalo Mini App, việc quản lý dữ liệu lưu trữ tuân theo cơ chế của nền tảng Zalo.

## 4. Thay đổi chính sách

Chính sách Cookie có thể được cập nhật theo thời gian, đồng thời với [Chính sách bảo mật](/privacy).

## 5. Liên hệ

Câu hỏi về việc sử dụng cookie vui lòng liên hệ **privacy@tutora.vn**.$md$
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO policy_documents (slug, title, summary, version, effective_date, status, display_order, published_at, content_markdown)
VALUES (
    'community-guidelines',
    'Quy tắc cộng đồng Tutora',
    'Chuẩn mực ứng xử áp dụng cho mọi Người dùng khi tương tác trên Tutora, và cách xử lý khi vi phạm.',
    '1.0',
    DATE '2026-08-07',
    'published',
    4,
    CURRENT_TIMESTAMP,
$md$Quy tắc cộng đồng áp dụng cho mọi Người dùng (Học viên, Phụ huynh, Gia sư) khi tương tác trên Tutora — qua hồ sơ, tin nhắn, buổi học video, đánh giá. Vi phạm quy tắc này bị xử lý theo mức độ, có thể dẫn đến cảnh cáo, tạm khóa hoặc chấm dứt tài khoản vĩnh viễn.

## 1. Tôn trọng và an toàn

- Đối xử tôn trọng, lịch sự với tất cả người dùng khác, không phân biệt độ tuổi, giới tính, tôn giáo, dân tộc, ngoại hình, hoàn cảnh.
- Nghiêm cấm quấy rối, đe dọa, bắt nạt, ngôn từ thù ghét, nội dung khiêu dâm hoặc gợi ý tình dục dưới mọi hình thức.
- Đặc biệt nghiêm cấm mọi hành vi tiếp cận, dụ dỗ hoặc có ý đồ không phù hợp với Học viên là trẻ vị thành niên — xem thêm Chính sách An toàn Trẻ em.

## 2. Trung thực

- Không mạo danh người khác, không sử dụng ảnh/thông tin giả để tạo hồ sơ.
- Gia sư không được phóng đại, làm giả bằng cấp, chứng chỉ, kinh nghiệm giảng dạy.
- Không viết đánh giá giả, mua bán đánh giá, hoặc yêu cầu người khác đánh giá sai sự thật.

## 3. Giao dịch minh bạch trên nền tảng

- Mọi giao dịch học phí phải thực hiện qua Ví Tutora. Không thỏa thuận thanh toán ngoài nền tảng, không trao đổi thông tin liên hệ cá nhân (số điện thoại, Zalo, mạng xã hội) nhằm né tránh phí dịch vụ trước khi hoàn tất ít nhất một buổi học chính thức qua nền tảng.
- Không lợi dụng trợ lý AI, tin nhắn hoặc buổi học để quảng cáo, bán hàng, tuyển dụng đa cấp hoặc nội dung không liên quan đến học tập.

## 4. Nội dung phù hợp

- Tài liệu, hình ảnh, video chia sẻ trong buổi học hoặc hồ sơ phải phù hợp với môi trường giáo dục, không chứa nội dung bạo lực, khiêu dâm, phân biệt đối xử, hoặc vi phạm pháp luật Việt Nam.
- Không đăng tải nội dung vi phạm bản quyền, tài liệu độc quyền của bên thứ ba khi chưa được phép.

## 5. Bảo mật thông tin người khác

- Không chụp màn hình, ghi âm/ghi hình trái phép buổi học ngoài chức năng ghi hình tích hợp của nền tảng, và không phát tán nội dung buổi học nếu chưa được các bên liên quan đồng ý.
- Không thu thập, lưu trữ hoặc chia sẻ thông tin cá nhân của người dùng khác (số điện thoại, địa chỉ, giấy tờ tùy thân...) ngoài mục đích sử dụng dịch vụ.

## 6. Cạnh tranh công bằng

- Không tạo nhiều tài khoản để thao túng xếp hạng, đánh giá hoặc chương trình khuyến mãi.
- Không can thiệp kỹ thuật (bot, script) vào hệ thống đặt lịch, thanh toán, đánh giá.

## 7. Báo cáo vi phạm

Người dùng phát hiện hành vi vi phạm quy tắc này có thể báo cáo qua nút "Báo cáo" trên hồ sơ/buổi học hoặc gửi email **trust-safety@tutora.vn**. Tutora cam kết xem xét và phản hồi mọi báo cáo trong thời gian hợp lý, bảo mật danh tính người báo cáo.

## 8. Hình thức xử lý vi phạm

| Mức độ | Ví dụ | Hình thức xử lý |
|---|---|---|
| Nhẹ | Chậm trễ, thiếu chuyên nghiệp | Nhắc nhở, cảnh cáo |
| Trung bình | Vi phạm lặp lại, giao dịch ngoài nền tảng | Tạm khóa tài khoản có thời hạn |
| Nghiêm trọng | Quấy rối, gian lận, xâm hại trẻ em, giả mạo danh tính | Khóa vĩnh viễn, báo cáo cơ quan chức năng nếu cần thiết |

Quyết định xử lý có thể được khiếu nại qua kênh hỗ trợ trong vòng 07 ngày.$md$
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO policy_documents (slug, title, summary, version, effective_date, status, display_order, published_at, content_markdown)
VALUES (
    'tutor-agreement',
    'Thỏa thuận hợp tác dành cho Gia sư',
    'Điều kiện, nghĩa vụ, hoa hồng và cách xử lý vi phạm áp dụng riêng cho Gia sư trên Tutora.',
    '1.0',
    DATE '2026-08-07',
    'published',
    5,
    CURRENT_TIMESTAMP,
$md$Thỏa thuận này áp dụng riêng cho các cá nhân đăng ký làm **Gia sư** trên nền tảng Tutora, bổ sung cho [Điều khoản sử dụng](/terms) chung.

## 1. Tính chất quan hệ hợp tác

1.1. Gia sư là **đối tác độc lập**, tự nguyện đăng ký cung cấp dịch vụ dạy học qua Tutora. Quan hệ giữa Gia sư và Tutora **không phải quan hệ lao động** theo Bộ luật Lao động; Tutora không đóng bảo hiểm xã hội, bảo hiểm y tế cho Gia sư.

1.2. Gia sư tự chịu trách nhiệm kê khai, nộp thuế thu nhập cá nhân (TNCN) đối với thu nhập nhận được qua nền tảng theo quy định pháp luật hiện hành. Tutora có thể cung cấp báo cáo thu nhập để hỗ trợ Gia sư kê khai khi được yêu cầu hợp lệ.

## 2. Điều kiện trở thành Gia sư

2.1. Đủ 18 tuổi trở lên, có năng lực hành vi dân sự đầy đủ.

2.2. Cung cấp đầy đủ, trung thực hồ sơ: trình độ học vấn, chứng chỉ, kinh nghiệm giảng dạy, video giới thiệu.

2.3. Hoàn tất xác minh danh tính (eKYC) bằng CCCD/CMND hợp lệ. Tutora có quyền từ chối hồ sơ nếu giấy tờ không hợp lệ, không rõ ràng hoặc có dấu hiệu giả mạo.

2.4. Hồ sơ được xét duyệt qua các trạng thái *Nháp → Chờ duyệt → Hoạt động/Bị từ chối*. Thời gian xét duyệt không cố định và phụ thuộc vào khối lượng hồ sơ tại từng thời điểm.

## 3. Nghĩa vụ khi giảng dạy

3.1. Giảng dạy đúng giờ, đúng nội dung đã cam kết với Học viên; thông báo trước ít nhất 24 giờ nếu cần đổi lịch (trừ trường hợp bất khả kháng).

3.2. Duy trì thái độ chuyên nghiệp, tôn trọng Học viên, đặc biệt với Học viên là trẻ vị thành niên — tuân thủ Chính sách An toàn Trẻ em.

3.3. Không được yêu cầu hoặc gợi ý Học viên thanh toán, liên hệ ngoài nền tảng Tutora nhằm né tránh phí dịch vụ. Vi phạm có thể bị khóa tài khoản vĩnh viễn và thu hồi số dư ví liên quan đến giao dịch vi phạm.

3.4. Không sử dụng buổi học để quảng cáo dịch vụ bên ngoài, bán sản phẩm không liên quan, hoặc thu thập thông tin cá nhân của Học viên ngoài phạm vi cần thiết cho việc dạy học.

3.5. Chịu trách nhiệm về nội dung, tài liệu giảng dạy do mình cung cấp, đảm bảo không vi phạm bản quyền hoặc pháp luật hiện hành.

## 4. Học phí, hoa hồng và thanh toán

4.1. Gia sư tự đề xuất mức học phí cho từng môn học, hiển thị công khai trên hồ sơ.

4.2. Tutora thu **phí dịch vụ (hoa hồng)** trên mỗi buổi học hoàn thành, được khấu trừ trước khi ghi nhận vào số dư khả dụng của Gia sư trong Ví Tutora. Tỷ lệ hoa hồng hiện hành được công bố trong mục Ví/Thu nhập trên nền tảng và có thể thay đổi theo thông báo trước.

4.3. Số dư chỉ được ghi nhận vào phần khả dụng để rút sau khi buổi học hoàn thành và không có khiếu nại trong thời hạn quy định tại Chính sách hủy lịch & hoàn tiền.

4.4. Gia sư đăng ký thông tin tài khoản ngân hàng chính chủ để nhận thanh toán rút tiền; Tutora không chịu trách nhiệm nếu Gia sư cung cấp sai thông tin tài khoản.

## 5. Đánh giá, xếp hạng và xử lý vi phạm

5.1. Gia sư được đánh giá bởi Học viên sau mỗi buổi học; điểm đánh giá ảnh hưởng đến thứ hạng hiển thị trên nền tảng.

5.2. Các hành vi sau có thể dẫn đến cảnh cáo, tạm ngưng hoặc chấm dứt tài khoản Gia sư:
- Hủy lịch nhiều lần không lý do chính đáng hoặc không xuất hiện buổi học;
- Bị nhiều Học viên phản ánh vi phạm quy tắc ứng xử;
- Gian lận đánh giá, thông đồng đặt lịch ảo để trục lợi;
- Vi phạm nghiêm trọng Điều khoản sử dụng hoặc [Quy tắc cộng đồng](/community-guidelines).

5.3. Gia sư có quyền khiếu nại quyết định xử lý vi phạm qua kênh hỗ trợ trong vòng 07 ngày kể từ khi nhận thông báo.

## 6. Chấm dứt hợp tác

6.1. Gia sư có thể ngừng nhận lịch dạy hoặc yêu cầu đóng tài khoản bất kỳ lúc nào, với điều kiện đã hoàn thành các buổi học đã đặt lịch hoặc thông báo hủy hợp lý cho Học viên liên quan.

6.2. Khi chấm dứt hợp tác, số dư khả dụng trong Ví sẽ được xử lý rút về tài khoản ngân hàng đã đăng ký theo quy trình thông thường, trừ trường hợp đang bị tạm giữ do khiếu nại/điều tra vi phạm.

## 7. Liên hệ

Câu hỏi liên quan đến hợp tác Gia sư vui lòng liên hệ **tutor-support@tutora.vn**.$md$
)
ON CONFLICT (slug) DO NOTHING;

COMMIT;

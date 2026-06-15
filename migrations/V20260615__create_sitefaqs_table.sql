-- V20260615: Create sitefaqs table
-- Purpose: Store FAQ content managed by Admin, displayed on public pages

CREATE TABLE IF NOT EXISTS sitefaqs (
    faqid       SERIAL PRIMARY KEY,
    question    TEXT         NOT NULL,
    answer      TEXT         NOT NULL,
    category    VARCHAR(50)  NOT NULL DEFAULT 'general',
    displayorder INT         NOT NULL DEFAULT 0,
    isactive    BOOLEAN      NOT NULL DEFAULT TRUE,
    createdat   TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updatedat   TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    createdby   VARCHAR(50),
    updatedby   VARCHAR(50),

    CONSTRAINT fk_sitefaqs_createdby FOREIGN KEY (createdby)
        REFERENCES users(userid) ON DELETE SET NULL,
    CONSTRAINT fk_sitefaqs_updatedby FOREIGN KEY (updatedby)
        REFERENCES users(userid) ON DELETE SET NULL
);

-- Index for public page query: active FAQs by category, ordered
CREATE INDEX IF NOT EXISTS idx_sitefaqs_category_active_order
    ON sitefaqs(category, isactive, displayorder ASC);

-- Index for admin management list
CREATE INDEX IF NOT EXISTS idx_sitefaqs_isactive_createdat
    ON sitefaqs(isactive, createdat DESC);

-- Table & column comments
COMMENT ON TABLE sitefaqs IS 'FAQ entries managed by Admin and displayed on public-facing pages';
COMMENT ON COLUMN sitefaqs.category IS 'FAQ group: general | tutor | parent | payment | booking';
COMMENT ON COLUMN sitefaqs.displayorder IS 'Sort order within the same category (ascending)';
COMMENT ON COLUMN sitefaqs.isactive IS 'Controls visibility on public page without deleting the record';
COMMENT ON COLUMN sitefaqs.createdby IS 'Admin userid who created this FAQ entry';
COMMENT ON COLUMN sitefaqs.updatedby IS 'Admin userid who last modified this FAQ entry';

-- Seed default FAQ entries
INSERT INTO sitefaqs (question, answer, category, displayorder) VALUES
(
    'Tutora là gì?',
    'Tutora là nền tảng kết nối học sinh và gia sư uy tín, hỗ trợ học tập trực tuyến và trực tiếp trên toàn quốc.',
    'general', 1
),
(
    'Làm thế nào để đăng ký tài khoản?',
    'Bạn có thể đăng ký bằng email tại trang Đăng ký. Sau khi điền thông tin, hệ thống sẽ gửi mã xác thực về email của bạn.',
    'general', 2
),
(
    'Gia sư có được xác minh không?',
    'Có. Tất cả gia sư đều phải nộp hồ sơ và trải qua quy trình xét duyệt bởi đội ngũ Tutora trước khi được hiển thị trên nền tảng.',
    'tutor', 1
),
(
    'Tôi có thể đặt lịch học như thế nào?',
    'Sau khi chọn gia sư phù hợp, bạn chọn khung giờ trống của gia sư và xác nhận đặt lịch. Gia sư sẽ xác nhận buổi học trong vòng 24 giờ.',
    'booking', 1
),
(
    'Phương thức thanh toán nào được hỗ trợ?',
    'Tutora hỗ trợ thanh toán qua ZaloPay và chuyển khoản ngân hàng (PayOS). Tiền học phí được giữ an toàn và chỉ giải ngân sau khi buổi học hoàn thành.',
    'payment', 1
),
(
    'Nếu buổi học bị hủy thì sao?',
    'Nếu gia sư hủy buổi học, học phí sẽ được hoàn trả toàn bộ về ví của bạn. Nếu học sinh hủy trước 24 giờ sẽ được hoàn 100%, hủy trong vòng 24 giờ sẽ hoàn 50%.',
    'payment', 2
);

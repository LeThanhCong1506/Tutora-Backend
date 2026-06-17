using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
namespace MV.ApplicationLayer.Services
{
    public class ExportService : IExportService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAppDbContext _context;

        public ExportService(IUnitOfWork unitOfWork, IAppDbContext context)
        {
            _unitOfWork = unitOfWork;
            _context = context;
        }

        // Múi giờ Việt Nam (UTC+7) – dùng cho export hiển thị (now using VietnamTimeHelper)

        // 1. Lấy danh sách HỌC SINH
        public async Task<StudentExportListResponse> GetStudentsForExportAsync()
        {
            //var allUsers = await _unitOfWork.AccountRepository.GetAccountsAsync();

            //var students = allUsers
            //    .Where(u => u.Roleid == (int)Roles.Student) // Lọc RoleId = 3
            //    .Select(u => new StudentExportResponse
            //    {
            //        Userid = u.Userid,
            //        Fullname = u.Fullname,
            //        Email = u.Email,
            //        Phone = u.Phone,
            //        Birthdate = u.Birthdate,
            //        Identitynumber = u.Identitynumber,
            //        Address = u.Address
            //    }).ToList();

            //return new StudentExportListResponse { Students = students };
            return null;
        }

        // 2. Lấy danh sách PHỤ HUYNH
        public async Task<ParentExportListResponse> GetParentsForExportAsync()
        {
            //var allUsers = await _unitOfWork.AccountRepository.GetAccountsAsync();

            //var parents = allUsers
            //    .Where(u => u.Roleid == (int)Roles.Parent) // Lọc RoleId = 4
            //    .Select(u => new ParentExportResponse
            //    {
            //        Userid = u.Userid,
            //        Fullname = u.Fullname,
            //        Email = u.Email,
            //        Phone = u.Phone,
            //        Birthdate = u.Birthdate,
            //        Identitynumber = u.Identitynumber,
            //        Address = u.Address
            //    }).ToList();

            //return new ParentExportListResponse { Parents = parents };
            return null;
        }

        // 3. Lấy MỘT MOCKTEST theo ID (và các câu hỏi của nó)
        public async Task<MockTestExportResponse> GetMockTestForExportAsync(int testId)
        {
            //var test = await _unitOfWork.MockTestRepository.GetMockTestByIdAsync(testId);
            //if (test == null)
            //{
            //    throw new KeyNotFoundException($"MockTest with ID {testId} not found or is inactive.");
            //}

            //var questionIds = test.Questions.Select(q => q.Questionid).ToList();

            //var questionsWithOptions = await _unitOfWork.QuestionRepository.GetQuestionsWithAnswersByIdsAsync(questionIds);
            //var questionDict = questionsWithOptions.ToDictionary(q => q.Questionid);

            //var mockTestDto = new MockTestExportResponse
            //{
            //    Testid = test.Testid,
            //    Testname = test.Testname,
            //    Durationminutes = test.Durationminutes,
            //    Subjectid = test.Subjectid
            //};

            //// Map từng câu hỏi
            //foreach (var testQuestion in test.Questions)
            //{
            //    if (questionDict.TryGetValue(testQuestion.Questionid, out var fullQuestion))
            //    {
            //        var questionDto = new QuestionExportResponse
            //        {
            //            Questionid = fullQuestion.Questionid,
            //            Content = fullQuestion.Content
            //        };

            //        // Map các lựa chọn của câu hỏi
            //        foreach (var option in fullQuestion.QuestionOptions)
            //        {
            //            questionDto.Options.Add(new OptionExportResponse
            //            {
            //                Iscorrect = option.Iscorrect,
            //                Optiontext = option.Optiontext
            //            });
            //        }
            //        mockTestDto.Questions.Add(questionDto);
            //    }
            //}

            //return mockTestDto;

            return null;
        }



        // 4. EXCEL: Lấy danh sách HỌC SINH
        public async Task<byte[]> GetStudentsForExportExcelAsync()
        {
            var dto = await GetStudentsForExportAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Students");
                worksheet.SheetView.FreezeRows(1);
                worksheet.TabColor = XLColor.Green;
                worksheet.Style.Font.FontName = "Calibri";

                worksheet.Cell(1, 1).Value = "UserId";
                worksheet.Cell(1, 2).Value = "Fullname";
                worksheet.Cell(1, 3).Value = "Email";
                worksheet.Cell(1, 4).Value = "Phone";
                worksheet.Cell(1, 5).Value = "Birthdate";
                worksheet.Cell(1, 6).Value = "Identitynumber";
                worksheet.Cell(1, 7).Value = "Address";

                var headerRange = worksheet.Range(1, 1, 1, 7);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#40B5A1");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                headerRange.Style.Border.BottomBorderColor = XLColor.Gray;

                int row = 2;
                foreach (var item in dto.Students)
                {
                    worksheet.Cell(row, 1).Value = item.Userid;
                    worksheet.Cell(row, 2).Value = item.Fullname;
                    worksheet.Cell(row, 3).Value = item.Email;
                    worksheet.Cell(row, 4).Value = item.Phone;
                    worksheet.Cell(row, 5).Value = item.Birthdate.HasValue ? item.Birthdate.Value.ToString("yyyy-MM-dd") : "";
                    worksheet.Cell(row, 6).Value = item.Identitynumber;
                    worksheet.Cell(row, 7).Value = item.Address;
                    row++;
                }

                // --- THAY ĐỔI Ở ĐÂY ---
                var fullRange = worksheet.Range(1, 1, row - 1, 7);
                fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                fullRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
                fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium; // Sửa từ Hair/Thin -> Medium
                fullRange.Style.Border.OutsideBorderColor = XLColor.Gray; // Đổi màu cho đậm
                // --- KẾT THÚC THAY ĐỔI ---

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        // 5. EXCEL: Lấy danh sách PHỤ HUYNH
        public async Task<byte[]> GetParentsForExportExcelAsync()
        {
            var dto = await GetParentsForExportAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Parents");
                worksheet.SheetView.FreezeRows(1);
                worksheet.TabColor = XLColor.Blue;
                worksheet.Style.Font.FontName = "Calibri";

                worksheet.Cell(1, 1).Value = "UserId";
                worksheet.Cell(1, 2).Value = "Fullname";
                worksheet.Cell(1, 3).Value = "Email";
                worksheet.Cell(1, 4).Value = "Phone";
                worksheet.Cell(1, 5).Value = "Identitynumber";
                worksheet.Cell(1, 6).Value = "Address";

                var headerRange = worksheet.Range(1, 1, 1, 6);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#3C8DBC");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                headerRange.Style.Border.BottomBorderColor = XLColor.Gray;

                int row = 2;
                foreach (var item in dto.Parents)
                {
                    worksheet.Cell(row, 1).Value = item.Userid;
                    worksheet.Cell(row, 2).Value = item.Fullname;
                    worksheet.Cell(row, 3).Value = item.Email;
                    worksheet.Cell(row, 4).Value = item.Phone;
                    worksheet.Cell(row, 5).Value = item.Identitynumber;
                    worksheet.Cell(row, 6).Value = item.Address;
                    row++;
                }

                // --- THAY ĐỔI Ở ĐÂY ---
                var fullRange = worksheet.Range(1, 1, row - 1, 6);
                fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                fullRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
                fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium; // Sửa từ Hair/Thin -> Medium
                fullRange.Style.Border.OutsideBorderColor = XLColor.Gray; // Đổi màu cho đậm
                // --- KẾT THÚC THAY ĐỔI ---

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        // 6. EXCEL: Lấy MOCKTEST theo ID
        public async Task<byte[]> GetMockTestForExportExcelAsync(int testId)
        {
            var dto = await GetMockTestForExportAsync(testId);

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("MockTest");
                worksheet.TabColor = XLColor.Orange;
                worksheet.Style.Font.FontName = "Calibri";

                // Thông tin chung
                worksheet.Cell("A1").Value = "Test ID:";
                worksheet.Cell("B1").Value = dto.Testid;
                worksheet.Cell("A2").Value = "Test Name:";
                worksheet.Cell("B2").Value = dto.Testname;
                worksheet.Cell("A3").Value = "Duration (minutes):";
                worksheet.Cell("B3").Value = dto.Durationminutes;
                worksheet.Cell("A4").Value = "Subject ID:";
                worksheet.Cell("B4").Value = dto.Subjectid;
                worksheet.Range("A1:A4").Style.Font.Bold = true;
                worksheet.Range("A1:B4").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF9E0");
                // --- THAY ĐỔI Ở ĐÂY ---
                worksheet.Range("A1:B4").Style.Border.OutsideBorder = XLBorderStyleValues.Medium; // Đổi thành Medium
                worksheet.Range("A1:B4").Style.Border.OutsideBorderColor = XLColor.Gray;
                // --- KẾT THÚC THAY ĐỔI ---


                // Header câu hỏi
                worksheet.Cell(6, 1).Value = "Question ID";
                worksheet.Cell(6, 2).Value = "Content";
                worksheet.Cell(6, 3).Value = "Option Text";
                worksheet.Cell(6, 4).Value = "Is Correct?";

                var headerRange = worksheet.Range(6, 1, 6, 4);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4B400");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                headerRange.Style.Border.BottomBorderColor = XLColor.Gray;

                worksheet.SheetView.FreezeRows(6);

                int row = 7;
                foreach (var q in dto.Questions)
                {
                    worksheet.Cell(row, 1).Value = q.Questionid;
                    worksheet.Cell(row, 2).Value = q.Content;
                    worksheet.Range(row, 1, row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

                    int startRowForMerge = row;

                    foreach (var opt in q.Options)
                    {
                        worksheet.Cell(row, 3).Value = opt.Optiontext;
                        worksheet.Cell(row, 4).Value = opt.Iscorrect;
                        worksheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        row++;
                    }

                    if (row - 1 > startRowForMerge)
                    {
                        worksheet.Range(startRowForMerge, 1, row - 1, 1).Merge();
                        worksheet.Range(startRowForMerge, 2, row - 1, 2).Merge();
                    }
                }

                // --- THAY ĐỔI Ở ĐÂY ---
                var fullRange = worksheet.Range(6, 1, row - 1, 4); // Bắt đầu từ header (dòng 6)
                fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                fullRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
                fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium; // Sửa từ Hair/Thin -> Medium
                fullRange.Style.Border.OutsideBorderColor = XLColor.Gray; // Đổi màu cho đậm
                // --- KẾT THÚC THAY ĐỔI ---

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        // =====================================================
        // TUTOR EXPORT METHODS (M3 - Replace Zalo OA)
        // =====================================================

        /// <summary>
        /// Export tutor lesson reports to Excel
        /// </summary>
        public async Task<byte[]> ExportTutorLessonReportsAsync(string tutorId, DateTime? fromDate, DateTime? toDate)
        {
            // Default: last 30 days if not specified
            var endDate = toDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var startDate = fromDate ?? endDate.AddDays(-30);

            // Normalize timezone
            var startUtc = startDate.Kind == DateTimeKind.Utc 
                ? startDate 
                : DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var endUtc = endDate.Kind == DateTimeKind.Utc 
                ? endDate 
                : DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

            var lessons = await _context.Lessons
                .Where(l => l.Tutorid == tutorId && l.Scheduledstart >= startUtc && l.Scheduledstart <= endUtc)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Tutorsubjectgradeprice)
                        .ThenInclude(p => p!.Subject)
                .Include(l => l.Student)
                .Include(l => l.Lessonreport)
                .OrderByDescending(l => l.Scheduledstart)
                .ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Lesson Reports");
                worksheet.SheetView.FreezeRows(1);
                worksheet.TabColor = XLColor.Green;
                worksheet.Style.Font.FontName = "Calibri";

                // Headers
                var headers = new[] { "Lesson ID", "Ngày học", "Học sinh", "Môn học", "Check-in", "Check-out",
                    "Nội dung buổi học", "BTVN", "Ghi chú", "HS có mặt", "Trạng thái", "Giá buổi (VND)", "Đã thanh toán" };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#40B5A1");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;

                int row = 2;
                foreach (var lesson in lessons)
                {
                    worksheet.Cell(row, 1).Value = lesson.Lessonid;
                    worksheet.Cell(row, 2).Value = lesson.Scheduledstart.ToString("dd/MM/yyyy HH:mm");
                    worksheet.Cell(row, 3).Value = lesson.Student?.Fullname ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 4).Value = lesson.Booking?.Subject?.Subjectname ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 5).Value = lesson.Checkintime?.ToString("HH:mm") ?? "-";
                    worksheet.Cell(row, 6).Value = lesson.Checkouttime?.ToString("HH:mm") ?? "-";
                    worksheet.Cell(row, 7).Value = lesson.Lessoncontent ?? lesson.Lessonreport?.Contentcovered ?? "";
                    worksheet.Cell(row, 8).Value = lesson.Homework ?? lesson.Lessonreport?.Homeworkassigned ?? "";
                    worksheet.Cell(row, 9).Value = lesson.Tutornotes ?? "";
                    worksheet.Cell(row, 10).Value = lesson.Isstudentpresent == true ? "Có" : "Không";
                    worksheet.Cell(row, 11).Value = lesson.Status ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 12).Value = lesson.Lessonprice ?? 0;
                    worksheet.Cell(row, 12).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(row, 13).Value = lesson.Issettled == true ? "Đã thanh toán" : "Chưa";
                    row++;
                }

                if (row > 2)
                {
                    var fullRange = worksheet.Range(1, 1, row - 1, headers.Length);
                    fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    fullRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
                    fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    fullRange.Style.Border.OutsideBorderColor = XLColor.Gray;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// Export tutor earnings/settlement history to Excel
        /// </summary>
        public async Task<byte[]> ExportTutorEarningsAsync(string tutorId, DateTime? fromDate, DateTime? toDate)
        {
            // Default: last 30 days if not specified
            var endDate = toDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var startDate = fromDate ?? endDate.AddDays(-30);

            // Normalize timezone
            var startUtc = startDate.Kind == DateTimeKind.Utc 
                ? startDate 
                : DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var endUtc = endDate.Kind == DateTimeKind.Utc 
                ? endDate 
                : DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

            // Get tutor's wallet
            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.Userid == tutorId);

            if (wallet == null)
            {
                // Return empty Excel if no wallet
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Earnings");
                    worksheet.Cell(1, 1).Value = "Không có dữ liệu thu nhập";
                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        return stream.ToArray();
                    }
                }
            }

            var transactions = await _context.Wallettransactions
                .Where(t => t.Walletid == wallet.Walletid && t.Createdat >= startUtc && t.Createdat <= endUtc)
                .OrderByDescending(t => t.Createdat)
                .ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Earnings");
                worksheet.SheetView.FreezeRows(1);
                worksheet.TabColor = XLColor.Blue;
                worksheet.Style.Font.FontName = "Calibri";

                // Summary section
                worksheet.Cell("A1").Value = "Tổng kết thu nhập";
                worksheet.Cell("A1").Style.Font.Bold = true;
                worksheet.Cell("A1").Style.Font.FontSize = 14;

                var totalIncome = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount ?? 0);
                var totalExpense = transactions.Where(t => t.Amount < 0).Sum(t => t.Amount ?? 0);

                worksheet.Cell("A2").Value = "Tổng thu:";
                worksheet.Cell("B2").Value = totalIncome;
                worksheet.Cell("B2").Style.NumberFormat.Format = "#,##0 VND";
                worksheet.Cell("B2").Style.Font.FontColor = XLColor.Green;

                worksheet.Cell("A3").Value = "Tổng chi:";
                worksheet.Cell("B3").Value = Math.Abs(totalExpense);
                worksheet.Cell("B3").Style.NumberFormat.Format = "#,##0 VND";
                worksheet.Cell("B3").Style.Font.FontColor = XLColor.Red;

                worksheet.Cell("A4").Value = "Số dư hiện tại:";
                worksheet.Cell("B4").Value = wallet.Balance ?? 0;
                worksheet.Cell("B4").Style.NumberFormat.Format = "#,##0 VND";
                worksheet.Cell("B4").Style.Font.Bold = true;

                worksheet.Range("A1:B4").Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
                worksheet.Range("A1:B4").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                // Headers (row 6)
                var headers = new[] { "Ngày", "Loại giao dịch", "Mã tham chiếu", "Số tiền (VND)", "Mô tả" };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(6, i + 1).Value = headers[i];
                }

                var headerRange = worksheet.Range(6, 1, 6, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#3C8DBC");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;

                worksheet.SheetView.FreezeRows(6);

                int row = 7;
                foreach (var trans in transactions)
                {
                    worksheet.Cell(row, 1).Value = trans.Createdat.HasValue ? trans.Createdat.Value.ToString("dd/MM/yyyy HH:mm") : "-";
                    worksheet.Cell(row, 2).Value = trans.Transactiontype ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 3).Value = trans.Referenceid?.ToString() ?? "-";
                    worksheet.Cell(row, 4).Value = trans.Amount ?? 0;
                    worksheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
                    if (trans.Amount < 0)
                        worksheet.Cell(row, 4).Style.Font.FontColor = XLColor.Red;
                    else
                        worksheet.Cell(row, 4).Style.Font.FontColor = XLColor.Green;
                    worksheet.Cell(row, 5).Value = trans.Description ?? "";
                    row++;
                }

                if (row > 7)
                {
                    var fullRange = worksheet.Range(6, 1, row - 1, headers.Length);
                    fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    fullRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
                    fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    fullRange.Style.Border.OutsideBorderColor = XLColor.Gray;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// Export tutor feedback history to Excel
        /// </summary>
        public async Task<byte[]> ExportTutorFeedbacksAsync(string tutorId, DateTime? fromDate, DateTime? toDate)
        {
            // Default: last 30 days if not specified
            var endDate = toDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var startDate = fromDate ?? endDate.AddDays(-30);

            // Normalize timezone
            var startUtc = startDate.Kind == DateTimeKind.Utc 
                ? startDate 
                : DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var endUtc = endDate.Kind == DateTimeKind.Utc 
                ? endDate 
                : DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

            var feedbacks = await _context.Feedbacks
                .Where(f => f.Touserid == tutorId && f.Createdat >= startUtc && f.Createdat <= endUtc && f.Isvisible == true)
                .Include(f => f.Fromuser)
                .Include(f => f.Lesson)
                    .ThenInclude(l => l!.Student)
                .Include(f => f.Booking)
                    .ThenInclude(b => b!.Tutorsubjectgradeprice)
                        .ThenInclude(p => p!.Subject)
                .OrderByDescending(f => f.Createdat)
                .ToListAsync();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Feedbacks");
                worksheet.SheetView.FreezeRows(1);
                worksheet.TabColor = XLColor.Orange;
                worksheet.Style.Font.FontName = "Calibri";

                // Summary
                var avgRating = feedbacks.Any() ? feedbacks.Average(f => f.Rating ?? 0) : 0;
                var totalFeedbacks = feedbacks.Count;

                worksheet.Cell("A1").Value = "Thống kê đánh giá";
                worksheet.Cell("A1").Style.Font.Bold = true;
                worksheet.Cell("A1").Style.Font.FontSize = 14;

                worksheet.Cell("A2").Value = "Tổng số đánh giá:";
                worksheet.Cell("B2").Value = totalFeedbacks;

                worksheet.Cell("A3").Value = "Rating trung bình:";
                worksheet.Cell("B3").Value = Math.Round(avgRating, 1);
                worksheet.Cell("B3").Style.Font.Bold = true;

                worksheet.Range("A1:B3").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF9E0");
                worksheet.Range("A1:B3").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                // Headers (row 5)
                var headers = new[] { "Ngày", "Phụ huynh", "Học sinh", "Môn học", "Rating", "Nội dung đánh giá", "Phản hồi của bạn", "Ngày phản hồi" };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(5, i + 1).Value = headers[i];
                }

                var headerRange = worksheet.Range(5, 1, 5, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4B400");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;

                worksheet.SheetView.FreezeRows(5);

                int row = 6;
                foreach (var feedback in feedbacks)
                {
                    worksheet.Cell(row, 1).Value = feedback.Createdat.HasValue ? feedback.Createdat.Value.ToString("dd/MM/yyyy") : "-";
                    worksheet.Cell(row, 2).Value = feedback.Fromuser?.Fullname ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 3).Value = feedback.Lesson?.Student?.Fullname ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 4).Value = feedback.Booking?.Subject?.Subjectname ?? feedback.Lesson?.Booking?.Subject?.Subjectname ?? DisplayValues.NotAvailable;
                    worksheet.Cell(row, 5).Value = feedback.Rating ?? 0;
                    worksheet.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    // Color code rating
                    if (feedback.Rating >= 4)
                        worksheet.Cell(row, 5).Style.Font.FontColor = XLColor.Green;
                    else if (feedback.Rating <= 2)
                        worksheet.Cell(row, 5).Style.Font.FontColor = XLColor.Red;

                    worksheet.Cell(row, 6).Value = feedback.Comment ?? "";
                    worksheet.Cell(row, 7).Value = feedback.Replycomment ?? "";
                    worksheet.Cell(row, 8).Value = feedback.Repliedat.HasValue ? feedback.Repliedat.Value.ToString("dd/MM/yyyy") : "-";
                    row++;
                }

                if (row > 6)
                {
                    var fullRange = worksheet.Range(5, 1, row - 1, headers.Length);
                    fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    fullRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
                    fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    fullRange.Style.Border.OutsideBorderColor = XLColor.Gray;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }
    }
}

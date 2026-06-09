using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel.Certificate;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Utilities;
using MV.DomainLayer.Constants;
using System.Text.RegularExpressions;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Service xử lý Phase 2: Credentials Auto-Validation
    /// Logic:
    /// 1. OCR Extract: Trích xuất CertificateName, Issuer, IssueDate, HolderName
    /// 2. Auto Validation:
    ///    - HolderName ≈ User.FullName (Fuzzy matching)
    ///    - AI Verify: Kiểm tra Issuer có hợp lệ (tổ chức tồn tại)
    ///    - So sánh Issuer OCR với User Input Issuer
    ///    - So sánh Year OCR với User Input Year
    /// 3. Quyết định:
    ///    - ALL PASS → Auto Verify
    ///    - ANY FAIL → PendingReview cho Admin
    /// </summary>
    public class CertificateVerificationService : ICertificateVerificationService
    {
        private readonly IOcrService _ocrService;
        private readonly ILogger<CertificateVerificationService> _logger;

        // Thresholds
        private const double NAME_MATCH_THRESHOLD = 0.5; // 50% - giảm để hỗ trợ so sánh tên có/không dấu
        private const double ISSUER_MATCH_THRESHOLD = 0.6; // 60% - threshold cho so sánh issuer OCR vs user input
        private const double OCR_CONFIDENCE_THRESHOLD = 60.0; // 60%
        private const double AI_ISSUER_CONFIDENCE_THRESHOLD = 50.0; // 50% - AI verification confidence

        public CertificateVerificationService(
            IOcrService ocrService,
            ILogger<CertificateVerificationService> logger)
        {
            _ocrService = ocrService;
            _logger = logger;
        }

        public async Task<CertificateValidationResult> AutoValidateCertificateAsync(
            Tutorcertificate certificate,
            string userFullName,
            Stream certificateFileStream,
            string fileName)
        {
            var result = new CertificateValidationResult
            {
                Details = new ValidationDetails
                {
                    ExpectedName = userFullName,
                    UserInputIssuer = certificate.Issuingorganization,
                    UserInputYear = certificate.Yearissued
                }
            };

            try
            {
                _logger.LogInformation("Starting auto-validation for certificate: {CertificateId}", certificate.Certificateid);

                // Step 1: OCR Extract
                var ocrResult = await _ocrService.ExtractTextFromImageAsync(certificateFileStream, fileName);

                result.OcrData = ocrResult.ExtractedData;
                result.Details.OcrSuccess = ocrResult.Success;
                result.Details.OcrConfidence = ocrResult.ConfidenceScore;

                if (!ocrResult.Success || ocrResult.ConfidenceScore < OCR_CONFIDENCE_THRESHOLD)
                {
                    result.Details.Errors.Add("The certificate image could not be read clearly. Please upload a higher quality image.");
                    _logger.LogWarning("OCR failed for certificate {CertificateId}", certificate.Certificateid);
                }

                // Step 2: Validate HolderName
                var extractedName = ocrResult.ExtractedData?.HolderName ?? "";
                result.Details.ExtractedName = extractedName;

                if (!string.IsNullOrWhiteSpace(extractedName) && !string.IsNullOrWhiteSpace(userFullName))
                {
                    var (nameMatched, nameSimilarity) = StringSimilarity.CompareNames(extractedName, userFullName, NAME_MATCH_THRESHOLD);
                    result.Details.NameMatched = nameMatched;
                    result.Details.NameSimilarity = nameSimilarity * 100;

                    if (!nameMatched)
                    {
                        result.Details.Errors.Add($"The name on the certificate does not match your profile name (similarity: {nameSimilarity * 100:F1}%). Please verify the certificate details.");
                    }
                }
                else
                {
                    // Không trích xuất được tên từ OCR → cần review
                    result.Details.NameMatched = false;
                    result.Details.Errors.Add("The holder's name could not be extracted from the certificate image.");
                }

                // Step 3: Validate Issuer
                var extractedIssuer = ocrResult.ExtractedData?.Issuer ?? "";
                var userInputIssuer = certificate.Issuingorganization ?? "";
                result.Details.ExtractedIssuer = extractedIssuer;
                result.Details.UserInputIssuer = userInputIssuer;

                await ValidateIssuerAsync(result, extractedIssuer, userInputIssuer);

                // Step 4: Validate Year - NEW LOGIC (tương tự Issuer)
                var extractedIssueDate = ocrResult.ExtractedData?.IssueDate ?? "";
                result.Details.ExtractedIssueDate = extractedIssueDate;

                ValidateDateIssued(result, extractedIssueDate, certificate.Yearissued);

                // Step 5: Final Decision
                var allPassed = result.Details.OcrSuccess &&
                                result.Details.NameMatched &&
                                result.Details.IssuerValidationPassed &&
                                result.Details.DateValidationPassed;

                if (allPassed)
                {
                    result.IsAutoVerified = true;
                    result.FinalStatus = CertificateStatus.Verified;
                    result.AdminNote = $"Auto-verified: Name={result.Details.NameSimilarity:F1}%, Issuer AI Valid={result.Details.IssuerValidByAi}, Issuer Match={result.Details.IssuerMatchSimilarity:F1}%, Year Match={result.Details.YearMatchesUserInput}";
                    result.Details.ValidationMessage = "All validation checks passed - Certificate auto-verified";

                    _logger.LogInformation("Certificate {CertificateId} auto-verified successfully", certificate.Certificateid);
                }
                else
                {
                    result.IsAutoVerified = false;
                    result.FinalStatus = CertificateStatus.PendingReview;

                    // Build admin note with details
                    var noteBuilder = new System.Text.StringBuilder();
                    noteBuilder.AppendLine("AI Auto-Validation Results:");
                    noteBuilder.AppendLine($"- OCR: {(result.Details.OcrSuccess ? "OK" : "FAILED")} ({result.Details.OcrConfidence:F1}%)");
                    noteBuilder.AppendLine($"- Name: {(result.Details.NameMatched ? "MATCHED" : "MISMATCH")} ({result.Details.NameSimilarity:F1}%)");
                    noteBuilder.AppendLine($"- Issuer AI Verify: {(result.Details.IssuerValidByAi ? "VALID" : "INVALID")} ({result.Details.AiIssuerConfidence:F1}% confidence)");
                    noteBuilder.AppendLine($"  Reason: {result.Details.AiIssuerVerificationReason ?? DisplayValues.NotAvailable}");
                    noteBuilder.AppendLine($"  Type: {result.Details.AiDetectedOrganizationType ?? DisplayValues.NotAvailable}");
                    noteBuilder.AppendLine($"- Issuer Match: {(result.Details.IssuerMatchesUserInput ? "MATCHED" : "MISMATCH")} ({result.Details.IssuerMatchSimilarity:F1}%)");
                    noteBuilder.AppendLine($"  Extracted: '{result.Details.ExtractedIssuer ?? DisplayValues.NotAvailable}'");
                    noteBuilder.AppendLine($"  User Input: '{result.Details.UserInputIssuer ?? DisplayValues.NotAvailable}'");
                    noteBuilder.AppendLine($"- Extracted Date Valid: {(result.Details.ExtractedDateIsValid ? "YES" : "NO")}");
                    noteBuilder.AppendLine($"- User Input Year Valid: {(result.Details.UserInputYearIsValid ? "YES" : "NO")}");
                    noteBuilder.AppendLine($"- Year Match: {(result.Details.YearMatchesUserInput ? "MATCHED" : "MISMATCH")}");
                    noteBuilder.AppendLine($"  Extracted Date: '{result.Details.ExtractedIssueDate ?? DisplayValues.NotAvailable}' → Year: {result.Details.ExtractedYear?.ToString() ?? DisplayValues.NotAvailable}");
                    noteBuilder.AppendLine($"  User Input Year: {result.Details.UserInputYear?.ToString() ?? DisplayValues.NotAvailable}");

                    if (result.Details.Errors.Any())
                    {
                        noteBuilder.AppendLine("\nIssues found:");
                        foreach (var error in result.Details.Errors)
                        {
                            noteBuilder.AppendLine($"  - {error}");
                        }
                    }

                    if (ocrResult.ExtractedData != null)
                    {
                        noteBuilder.AppendLine("\nOCR Extracted Data:");
                        noteBuilder.AppendLine($"  - Holder: {ocrResult.ExtractedData.HolderName ?? DisplayValues.NotAvailable}");
                        noteBuilder.AppendLine($"  - Issuer: {ocrResult.ExtractedData.Issuer ?? DisplayValues.NotAvailable}");
                        noteBuilder.AppendLine($"  - Date: {ocrResult.ExtractedData.IssueDate ?? DisplayValues.NotAvailable}");
                        noteBuilder.AppendLine($"  - Type: {ocrResult.ExtractedData.DetectedType ?? DisplayValues.NotAvailable}");
                    }

                    result.AdminNote = noteBuilder.ToString();
                    result.Details.ValidationMessage = $"Validation failed: {result.Details.Errors.Count} issue(s) found";

                    _logger.LogInformation("Certificate {CertificateId} requires admin review: {Errors}",
                        certificate.Certificateid, string.Join(", ", result.Details.Errors));
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during certificate auto-validation for {CertificateId}", certificate.Certificateid);

                result.IsAutoVerified = false;
                result.FinalStatus = CertificateStatus.PendingReview;
                result.AdminNote = $"Auto-validation error: {ex.Message}";
                result.Details.Errors.Add("An error occurred during verification. Please try again or contact support.");

                return result;
            }
        }

        /// <summary>
        /// Validate Issuer với 2 bước:
        /// 1. AI Verify: Kiểm tra tổ chức có tồn tại và hợp lệ không
        /// 2. So sánh: Issuer từ OCR phải khớp với Issuer user nhập
        /// </summary>
        private async Task ValidateIssuerAsync(CertificateValidationResult result, string extractedIssuer, string userInputIssuer)
        {
            // Bước 1: AI Verify issuer từ OCR
            if (!string.IsNullOrWhiteSpace(extractedIssuer))
            {
                var aiVerifyResult = await _ocrService.VerifyIssuerAsync(extractedIssuer);
                result.Details.IssuerValidByAi = aiVerifyResult.IsValid && aiVerifyResult.Confidence >= AI_ISSUER_CONFIDENCE_THRESHOLD;
                result.Details.AiIssuerVerificationReason = aiVerifyResult.Reason;
                result.Details.AiIssuerConfidence = aiVerifyResult.Confidence;
                result.Details.AiDetectedOrganizationType = aiVerifyResult.OrganizationType;

                if (!result.Details.IssuerValidByAi)
                {
                    result.Details.Errors.Add($"The issuing organization '{extractedIssuer}' could not be verified as a legitimate organization. Reason: {aiVerifyResult.Reason}");
                }
            }
            else
            {
                // Không extract được issuer từ OCR
                result.Details.IssuerValidByAi = false;
                result.Details.AiIssuerVerificationReason = "Could not extract issuer from certificate";
                result.Details.Errors.Add("The issuing organization could not be extracted from the certificate image.");
            }

            // Bước 2: So sánh issuer OCR với user input
            if (!string.IsNullOrWhiteSpace(extractedIssuer) && !string.IsNullOrWhiteSpace(userInputIssuer))
            {
                // Sử dụng fuzzy matching để so sánh
                var similarity = StringSimilarity.CalculateSimilarity(
                    StringSimilarity.RemoveVietnameseDiacritics(extractedIssuer.ToLowerInvariant()),
                    StringSimilarity.RemoveVietnameseDiacritics(userInputIssuer.ToLowerInvariant())
                );

                result.Details.IssuerMatchSimilarity = similarity * 100;
                result.Details.IssuerMatchesUserInput = similarity >= ISSUER_MATCH_THRESHOLD;

                if (!result.Details.IssuerMatchesUserInput)
                {
                    result.Details.Errors.Add($"The issuer on the certificate ('{extractedIssuer}') does not match what you entered ('{userInputIssuer}'). Similarity: {similarity * 100:F1}%");
                }
            }
            else if (string.IsNullOrWhiteSpace(userInputIssuer))
            {
                result.Details.IssuerMatchesUserInput = false;
                result.Details.Errors.Add("The issuing organization was not provided in the form.");
            }
            else
            {
                // Không có extracted issuer - không thể so sánh, nhưng đã có lỗi ở bước 1
                result.Details.IssuerMatchesUserInput = false;
            }

            // Kết quả tổng hợp: cả 2 bước đều phải pass
            result.Details.IssuerValidationPassed = result.Details.IssuerValidByAi && result.Details.IssuerMatchesUserInput;
        }

        /// <summary>
        /// Validate Date/Year với 3 bước:
        /// 1. Parse và kiểm tra ngày từ OCR có hợp lệ không (không phải tương lai)
        /// 2. Kiểm tra năm user input hợp lệ (1950 đến hiện tại)
        /// 3. So sánh năm từ OCR với năm user nhập
        /// </summary>
        private void ValidateDateIssued(CertificateValidationResult result, string extractedIssueDate, int? userInputYear)
        {
            result.Details.ExtractedIssueDate = extractedIssueDate;
            result.Details.UserInputYear = userInputYear;

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var currentYear = now.Year;

            // Bước 1: Parse ngày từ OCR và kiểm tra có hợp lệ không
            var (extractedDate, extractedYear) = ParseDateFromString(extractedIssueDate);
            result.Details.ExtractedDate = extractedDate;
            result.Details.ExtractedYear = extractedYear;

            if (extractedDate.HasValue)
            {
                // Ngày không được là tương lai
                result.Details.ExtractedDateIsValid = extractedDate.Value <= now;

                if (!result.Details.ExtractedDateIsValid)
                {
                    result.Details.Errors.Add($"The issue date on the certificate ({extractedDate.Value:yyyy-MM-dd}) is in the future. This is not allowed.");
                }
            }
            else if (extractedYear.HasValue)
            {
                // Chỉ có năm, kiểm tra năm không phải tương lai
                result.Details.ExtractedDateIsValid = extractedYear.Value <= currentYear && extractedYear.Value >= 1950;

                if (!result.Details.ExtractedDateIsValid)
                {
                    result.Details.Errors.Add($"The issue year on the certificate ({extractedYear}) is invalid. It must be between 1950 and {currentYear}.");
                }
            }
            else
            {
                // Không extract được ngày/năm từ OCR
                result.Details.ExtractedDateIsValid = false;
                result.Details.Errors.Add("Could not extract a valid issue date from the certificate image.");
            }

            // Bước 2: Kiểm tra năm user input có hợp lệ không
            if (userInputYear.HasValue)
            {
                result.Details.UserInputYearIsValid = userInputYear.Value >= 1950 && userInputYear.Value <= currentYear;

                if (!result.Details.UserInputYearIsValid)
                {
                    result.Details.Errors.Add($"The year you entered ({userInputYear}) is invalid. It must be between 1950 and {currentYear}.");
                }
            }
            else
            {
                // Năm không được cung cấp - coi như valid (optional field)
                result.Details.UserInputYearIsValid = true;
            }

            // Bước 3: So sánh năm OCR với năm user input
            if (extractedYear.HasValue && userInputYear.HasValue)
            {
                result.Details.YearMatchesUserInput = extractedYear.Value == userInputYear.Value;

                if (!result.Details.YearMatchesUserInput)
                {
                    result.Details.Errors.Add($"The year on the certificate ({extractedYear}) does not match what you entered ({userInputYear}).");
                }
            }
            else if (!extractedYear.HasValue && userInputYear.HasValue)
            {
                // Không extract được năm từ OCR, nhưng user có nhập - cần review
                result.Details.YearMatchesUserInput = false;
                // Lỗi đã được thêm ở bước 1
            }
            else if (!userInputYear.HasValue)
            {
                // User không nhập năm - skip comparison, coi như matched
                result.Details.YearMatchesUserInput = true;
            }
            else
            {
                result.Details.YearMatchesUserInput = true; // Cả 2 đều null
            }

            // Kết quả tổng hợp: ExtractedDateIsValid + UserInputYearIsValid + YearMatchesUserInput
            result.Details.DateValidationPassed = result.Details.ExtractedDateIsValid &&
                                                   result.Details.UserInputYearIsValid &&
                                                   result.Details.YearMatchesUserInput;
        }

        /// <summary>
        /// Parse ngày tháng từ chuỗi với nhiều format khác nhau
        /// Trả về tuple (DateTime?, int?) - ngày đầy đủ nếu có, hoặc chỉ năm
        /// </summary>
        private (DateTime? fullDate, int? yearOnly) ParseDateFromString(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return (null, null);

            // Thử parse như DateTime đầy đủ trước
            string[] dateFormats = {
                "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "d/M/yyyy",
                "yyyy/MM/dd", "dd-MM-yyyy", "MM-dd-yyyy",
                "MMMM d, yyyy", "MMMM dd, yyyy", "d MMMM yyyy", "dd MMMM yyyy",
                "MMM d, yyyy", "MMM dd, yyyy", "d MMM yyyy", "dd MMM yyyy"
            };

            foreach (var format in dateFormats)
            {
                if (DateTime.TryParseExact(dateString.Trim(), format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    return (parsedDate, parsedDate.Year);
                }
            }

            // Thử parse với TryParse thông thường
            if (DateTime.TryParse(dateString, out var parsed))
            {
                return (parsed, parsed.Year);
            }

            // Không parse được ngày đầy đủ - thử extract năm
            // Pattern 1: Tìm năm 4 chữ số (19xx hoặc 20xx)
            var yearMatch = Regex.Match(dateString, @"\b(19|20)\d{2}\b");
            if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
            {
                return (null, year);
            }

            // Pattern 2: Tìm số có 4 chữ số bất kỳ
            var anyFourDigits = Regex.Match(dateString, @"\d{4}");
            if (anyFourDigits.Success && int.TryParse(anyFourDigits.Value, out var anyYear))
            {
                if (anyYear >= 1950 && anyYear <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Year + 1)
                    return (null, anyYear);
            }

            return (null, null);
        }
    }
}


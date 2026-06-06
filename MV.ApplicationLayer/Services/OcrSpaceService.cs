using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace MV.ApplicationLayer.Services
{
    public class OcrSpaceService : IOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OcrSpaceService> _logger;
        private readonly string _apiKey;
        private readonly string _groqApiKey;
        private const string API_URL = "https://api.ocr.space/parse/image";
        private const string GROQ_API_URL = "https://api.groq.com/openai/v1/chat/completions";
        private const int MAX_FILE_SIZE_KB = 1024;
        private const int TARGET_FILE_SIZE_KB = 900;

        public OcrSpaceService(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<OcrSpaceService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = config["OcrSpace:ApiKey"] ?? "";
            _groqApiKey = config["Groq:ApiKey"] ?? "";
        }

        public async Task<OcrGenericResponse> ExtractTextFromImageAsync(Stream imageStream, string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogError("OCR.space API key is not configured");
                    return new OcrGenericResponse
                    {
                        Success = false,
                        ErrorMessage = "OCR.space API key not configured"
                    };
                }

                using var memoryStream = new MemoryStream();
                await imageStream.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
                var fileSizeKB = imageBytes.Length / 1024.0;

                _logger.LogInformation("Original file: {FileName}, Size: {Size:F2} KB", fileName, fileSizeKB);

                if (fileSizeKB > MAX_FILE_SIZE_KB && IsImageFile(fileExtension))
                {
                    _logger.LogWarning("File exceeds {Limit}KB limit. Attempting compression...", MAX_FILE_SIZE_KB);
                    imageBytes = CompressImage(imageBytes, TARGET_FILE_SIZE_KB);
                    fileSizeKB = imageBytes.Length / 1024.0;
                    _logger.LogInformation("After compression: {Size:F2} KB", fileSizeKB);

                    if (fileSizeKB > MAX_FILE_SIZE_KB)
                    {
                        return new OcrGenericResponse
                        {
                            Success = false,
                            ErrorMessage = $"File too large ({fileSizeKB:F0}KB). Max allowed: {MAX_FILE_SIZE_KB}KB. Please use a smaller image."
                        };
                    }
                }
                else if (fileSizeKB > MAX_FILE_SIZE_KB && fileExtension == ".pdf")
                {
                    return new OcrGenericResponse
                    {
                        Success = false,
                        ErrorMessage = $"PDF file too large ({fileSizeKB:F0}KB). Max allowed: {MAX_FILE_SIZE_KB}KB. Please compress the PDF or convert to image."
                    };
                }

                var base64Image = Convert.ToBase64String(imageBytes);
                var mimeType = GetMimeType(fileExtension);

                var formData = new MultipartFormDataContent
                {
                    { new StringContent($"data:{mimeType};base64,{base64Image}"), "base64Image" },
                    { new StringContent("eng"), "language" },
                    { new StringContent("1"), "OCREngine" },
                    { new StringContent("true"), "isTable" },
                    { new StringContent("true"), "scale" },
                    { new StringContent("true"), "detectOrientation" },
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("apikey", _apiKey);

                var response = await _httpClient.PostAsync(API_URL, formData);
                var responseString = await response.Content.ReadAsStringAsync();

                _logger.LogDebug("OCR.space raw response: {Response}", responseString);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OCR.space API failed with status {StatusCode}: {Response}",
                        response.StatusCode, responseString);
                    return new OcrGenericResponse
                    {
                        Success = false,
                        ErrorMessage = $"API error: {response.StatusCode}"
                    };
                }

                var ocrResponse = JsonSerializer.Deserialize<OcrSpaceApiResponse>(responseString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (ocrResponse == null || ocrResponse.IsErroredOnProcessing)
                {
                    var errorMsg = ocrResponse?.GetErrorMessage() ?? "Unknown error";
                    _logger.LogWarning("OCR.space processing error: {Error}", errorMsg);
                    return new OcrGenericResponse
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                if (ocrResponse.ParsedResults == null || !ocrResponse.ParsedResults.Any())
                {
                    _logger.LogWarning("OCR.space returned no results");
                    return new OcrGenericResponse
                    {
                        Success = false,
                        ErrorMessage = "No text detected in image"
                    };
                }

                var rawText = string.Join("\n", ocrResponse.ParsedResults
                    .Where(p => !string.IsNullOrWhiteSpace(p.ParsedText))
                    .Select(p => p.ParsedText));

                var confidence = CalculateConfidence(ocrResponse.ParsedResults);

                _logger.LogInformation("OCR.space extracted {Length} characters with {Confidence:F1}% confidence",
                    rawText.Length, confidence);

                // Sử dụng Groq AI để parse certificate data
                var extractedData = await ParseCertificateDataWithGroqAsync(rawText);

                return new OcrGenericResponse
                {
                    Success = true,
                    ConfidenceScore = confidence,
                    RawText = rawText,
                    ExtractedData = extractedData
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OCR.space API for file: {FileName}", fileName);
                return new OcrGenericResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Sử dụng Groq AI (Llama 3.3 70B) để trích xuất thông tin từ certificate text
        /// </summary>
        private async Task<CertificateOcrData> ParseCertificateDataWithGroqAsync(string rawText)
        {
            var data = new CertificateOcrData
            {
                FieldConfidences = new Dictionary<string, double>()
            };

            if (string.IsNullOrWhiteSpace(rawText))
                return data;

            if (string.IsNullOrEmpty(_groqApiKey))
            {
                _logger.LogWarning("Groq API key not configured, returning empty data");
                return data;
            }

            try
            {
                _logger.LogInformation("=== RAW OCR TEXT ===\n{RawText}\n=== END ===", rawText);

                var prompt = $$"""
                    You are an expert at extracting information from certificate/credential documents.
                    
                    === OCR TEXT START ===
                    {{rawText}}
                    === OCR TEXT END ===

                    CRITICAL RULES FOR holderName:
                    
                    The certificate HOLDER is the person who EARNED/COMPLETED the certificate.
                    
                    PATTERN TO IDENTIFY HOLDER:
                    - The holder's name appears IMMEDIATELY BEFORE phrases like:
                      * "has successfully completed"
                      * "has completed"  
                      * "awarded to"
                      * "presented to"
                      * "certify that"
                    - The holder's name does NOT have any title attached (no "CEO", "Co-Founder", "Director", "Instructor", etc.)
                    
                    PATTERN TO IDENTIFY NON-HOLDERS (EXCLUDE THESE):
                    - Names followed by titles like: ", Co-Founder", ", CEO", ", Founder", ", Director", ", Instructor"
                    - Names that appear AFTER another name on the same line with a title
                    - Names at the signature area (bottom of certificate)
                    
                    EXAMPLE:
                    If text shows: "Le Thanh Cong     Abhay Gupta, Co-Founder, Board Infinity has successfully completed"
                    - HOLDER = "Le Thanh Cong" (no title, before "has successfully completed")
                    - NOT HOLDER = "Abhay Gupta" (has "Co-Founder" title = signatory)

                    EXTRACTION:
                    1. **holderName**: Person who completed certificate (NO title attached to their name)
                    2. **issuer**: Organization (e.g., Coursera, Board Infinity)
                    3. **issueDate**: Date found in text
                    4. **expiryDate**: Expiration date or null
                    5. **certificateName**: Course/Specialization name
                    6. **credentialId**: ID from verify URL or certificate
                    7. **detectedType**: "degree"/"language"/"badge"/"tech_certification"/"professional"/"other"

                    Return ONLY valid JSON:
                    {"holderName":"name","issuer":"issuer","issueDate":"date","expiryDate":null,"certificateName":"course","credentialId":null,"detectedType":"type"}
                    """;


                var requestBody = new GroqRequest
                {
                    Model = "llama-3.3-70b-versatile",
                    Messages =
                    [
                new GroqMessage { Role = AiChatRole.System, Content = "You extract certificate holder information. The holder NEVER has titles like Co-Founder, CEO, Director attached to their name." },
                new GroqMessage { Role = AiChatRole.User, Content = prompt }
                    ],
                    Temperature = 0.0, // Giảm temperature để output ổn định hơn
                    MaxTokens = 500
                };

                var jsonRequest = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, GROQ_API_URL);
            request.Headers.Authorization = new AuthenticationHeaderValue(HttpConstants.BearerScheme, _groqApiKey);
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, HttpConstants.JsonContentType);

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Groq API error: {StatusCode} - {Response}", response.StatusCode, responseString);
                    return data;
                }

                var groqResponse = JsonSerializer.Deserialize<GroqResponse>(responseString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var jsonResponse = groqResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";

                _logger.LogInformation("Groq raw response: {Response}", jsonResponse);

                jsonResponse = CleanJsonResponse(jsonResponse);

                if (!string.IsNullOrEmpty(jsonResponse))
                {
                    var parsed = JsonSerializer.Deserialize<CertificateExtractedResponse>(jsonResponse,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsed != null)
                    {
                        data.HolderName = parsed.HolderName?.Trim();
                        data.Issuer = parsed.Issuer?.Trim();
                        data.IssueDate = parsed.IssueDate?.Trim();
                        data.ExpiryDate = parsed.ExpiryDate?.Trim();
                        data.CertificateName = parsed.CertificateName?.Trim();
                        data.CredentialId = parsed.CredentialId?.Trim();
                        data.DetectedType = parsed.DetectedType?.Trim() ?? "other";

                        if (!string.IsNullOrEmpty(data.HolderName))
                            data.FieldConfidences["HolderName"] = 85;
                        if (!string.IsNullOrEmpty(data.Issuer))
                            data.FieldConfidences["Issuer"] = 85;
                        if (!string.IsNullOrEmpty(data.IssueDate))
                            data.FieldConfidences["IssueDate"] = 80;
                        if (!string.IsNullOrEmpty(data.CertificateName))
                            data.FieldConfidences["CertificateName"] = 80;

                        _logger.LogInformation(
                            "Groq extracted - HolderName: '{HolderName}', Issuer: '{Issuer}', Date: '{Date}', Certificate: '{Cert}', Type: '{Type}'",
                            data.HolderName ?? "NULL",
                            data.Issuer ?? "NULL",
                            data.IssueDate ?? "NULL",
                            data.CertificateName ?? "NULL",
                            data.DetectedType);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Groq API for certificate parsing");
            }

            return data;
        }

        private static string CleanJsonResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return response;

            response = Regex.Replace(response, @"```json\s*", "", RegexOptions.IgnoreCase);
            response = Regex.Replace(response, @"```\s*", "");
            response = Regex.Replace(response, @"\s*```", "");

            var startIndex = response.IndexOf('{');
            var endIndex = response.LastIndexOf('}');

            if (startIndex >= 0 && endIndex > startIndex)
            {
                response = response.Substring(startIndex, endIndex - startIndex + 1);
            }

            return response.Trim();
        }

        /// <summary>
        /// Xác thực issuer bằng Groq AI - kiểm tra tổ chức có tồn tại và hợp lệ không
        /// </summary>
        public async Task<IssuerVerificationResult> VerifyIssuerAsync(string issuer)
        {
            var result = new IssuerVerificationResult
            {
                IsValid = false,
                Confidence = 0
            };

            if (string.IsNullOrWhiteSpace(issuer))
            {
                result.Reason = "Issuer is empty or null";
                return result;
            }

            if (string.IsNullOrEmpty(_groqApiKey))
            {
                _logger.LogWarning("Groq API key not configured, cannot verify issuer");
                result.Reason = "AI verification service not configured";
                return result;
            }

            try
            {
                _logger.LogInformation("Verifying issuer with AI: {Issuer}", issuer);

                var prompt = $$"""
                    You are an expert at verifying educational and professional certification organizations.
                    
                    TASK: Verify if the following organization is a legitimate issuer of certificates, degrees, or professional credentials.
                    
                    ORGANIZATION TO VERIFY: "{{issuer}}"
                    
                    VERIFICATION CRITERIA:
                    1. Does this organization actually exist?
                    2. Is it a legitimate educational institution, certification body, or training organization?
                    3. Is it recognized/accredited in its field?
                    4. Types of valid organizations include:
                       - Universities and colleges (public/private)
                       - Online learning platforms (Coursera, Udemy, edX, etc.)
                       - Technology companies offering certifications (Microsoft, Google, AWS, etc.)
                       - Language testing bodies (British Council, ETS, JLPT, etc.)
                       - Professional certification bodies (PMI, CFA Institute, etc.)
                       - Training centers and academies
                       - Government education ministries
                    
                    RETURN ONLY valid JSON with no additional text:
                    {
                        "isValid": true/false,
                        "reason": "Brief explanation why valid or invalid",
                        "confidence": 0-100,
                        "officialName": "Official name of organization if known, or null",
                        "organizationType": "university|online_platform|tech_company|language_body|professional_body|training_center|government|unknown"
                    }
                    
                    IMPORTANT:
                    - isValid = true if the organization is a real, legitimate issuer of credentials
                    - isValid = false ONLY if the organization doesn't exist OR is clearly fraudulent
                    - For well-known organizations, confidence should be 85-100
                    - For lesser-known but valid organizations, confidence should be 60-84
                    - If uncertain, set isValid = true with lower confidence (50-70) to avoid false rejections
                    """;

                var requestBody = new GroqRequest
                {
                    Model = "llama-3.3-70b-versatile",
                    Messages =
                    [
                new GroqMessage { Role = AiChatRole.System, Content = "You verify educational and certification organizations. Be lenient - only reject clearly fake or non-existent organizations." },
                new GroqMessage { Role = AiChatRole.User, Content = prompt }
                    ],
                    Temperature = 0.0,
                    MaxTokens = 300
                };

                var jsonRequest = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, GROQ_API_URL);
            request.Headers.Authorization = new AuthenticationHeaderValue(HttpConstants.BearerScheme, _groqApiKey);
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, HttpConstants.JsonContentType);

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Groq API error for issuer verification: {StatusCode} - {Response}", response.StatusCode, responseString);
                    result.Reason = "AI verification service error";
                    return result;
                }

                var groqResponse = JsonSerializer.Deserialize<GroqResponse>(responseString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var jsonResponse = groqResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";

                _logger.LogInformation("Groq issuer verification response: {Response}", jsonResponse);

                jsonResponse = CleanJsonResponse(jsonResponse);

                if (!string.IsNullOrEmpty(jsonResponse))
                {
                    var parsed = JsonSerializer.Deserialize<IssuerVerificationAiResponse>(jsonResponse,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsed != null)
                    {
                        result.IsValid = parsed.IsValid;
                        result.Reason = parsed.Reason?.Trim();
                        result.Confidence = parsed.Confidence;
                        result.OfficialName = parsed.OfficialName?.Trim();
                        result.OrganizationType = parsed.OrganizationType?.Trim();

                        _logger.LogInformation(
                            "Issuer verification result - IsValid: {IsValid}, Confidence: {Confidence}%, Reason: {Reason}",
                            result.IsValid, result.Confidence, result.Reason);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Groq API for issuer verification");
                result.Reason = "Error during AI verification";
            }

            return result;
        }

        private byte[] CompressImage(byte[] imageBytes, int targetSizeKB)
        {
            try
            {
                using var inputStream = new MemoryStream(imageBytes);
                using var original = SKBitmap.Decode(inputStream);

                if (original == null)
                    return imageBytes;

                var maxDimension = 2000;
                var bitmap = original;

                if (original.Width > maxDimension || original.Height > maxDimension)
                {
                    var scale = Math.Min((float)maxDimension / original.Width, (float)maxDimension / original.Height);
                    var newWidth = (int)(original.Width * scale);
                    var newHeight = (int)(original.Height * scale);
                    bitmap = original.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
                    _logger.LogDebug("Resized image from {OW}x{OH} to {NW}x{NH}",
                        original.Width, original.Height, newWidth, newHeight);
                }

                using var image = SKImage.FromBitmap(bitmap);

                for (int quality = 85; quality >= 20; quality -= 10)
                {
                    using var outputStream = new MemoryStream();
                    var encodeResult = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                    encodeResult.SaveTo(outputStream);

                    var resultBytes = outputStream.ToArray();
                    var resultSizeKB = resultBytes.Length / 1024.0;

                    _logger.LogDebug("Compression attempt - Quality: {Q}%, Size: {S:F2}KB", quality, resultSizeKB);

                    if (resultSizeKB <= targetSizeKB)
                    {
                        _logger.LogInformation("Compressed successfully with quality {Q}%", quality);
                        return resultBytes;
                    }
                }

                var finalScale = 0.5f;
                var finalWidth = (int)(bitmap.Width * finalScale);
                var finalHeight = (int)(bitmap.Height * finalScale);
                using var smallBitmap = bitmap.Resize(new SKImageInfo(finalWidth, finalHeight), SKFilterQuality.Medium);
                using var smallImage = SKImage.FromBitmap(smallBitmap);
                using var finalStream = new MemoryStream();
                smallImage.Encode(SKEncodedImageFormat.Jpeg, 50).SaveTo(finalStream);

                return finalStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Image compression failed, using original");
                return imageBytes;
            }
        }

        private static bool IsImageFile(string extension) => extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".tif" => true,
            _ => false
        };

        private static string GetMimeType(string extension) => extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".webp" => "image/webp",
            _ => "image/png"
        };

        private static double CalculateConfidence(List<OcrParsedResult> results)
        {
            var successCount = results.Count(r => r.FileParseExitCode == 1);
            if (successCount == results.Count)
                return 85.0;
            return 65.0;
        }

        #region Response Models

        // Groq Request/Response Models
        private class GroqRequest
        {
            public string Model { get; set; } = "";
            public List<GroqMessage> Messages { get; set; } = [];
            public double Temperature { get; set; }
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
        }

        private class GroqMessage
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private class GroqResponse
        {
            public List<GroqChoice>? Choices { get; set; }
        }

        private class GroqChoice
        {
            public GroqMessage? Message { get; set; }
        }

        // Certificate Response Model
        private class CertificateExtractedResponse
        {
            public string? HolderName { get; set; }
            public string? Issuer { get; set; }
            public string? IssueDate { get; set; }
            public string? ExpiryDate { get; set; }
            public string? CertificateName { get; set; }
            public string? CredentialId { get; set; }
            public string? DetectedType { get; set; }
        }

        // Issuer Verification AI Response Model
        private class IssuerVerificationAiResponse
        {
            public bool IsValid { get; set; }
            public string? Reason { get; set; }
            public double Confidence { get; set; }
            public string? OfficialName { get; set; }
            public string? OrganizationType { get; set; }
        }

        // OCR.space Response Models
        private class OcrSpaceApiResponse
        {
            public List<OcrParsedResult>? ParsedResults { get; set; }
            public int OCRExitCode { get; set; }
            public bool IsErroredOnProcessing { get; set; }
            public JsonElement ErrorMessage { get; set; }
            public JsonElement ErrorDetails { get; set; }

            public string? GetErrorMessage()
            {
                if (ErrorMessage.ValueKind == JsonValueKind.Undefined ||
                    ErrorMessage.ValueKind == JsonValueKind.Null)
                    return null;

                if (ErrorMessage.ValueKind == JsonValueKind.String)
                    return ErrorMessage.GetString();

                if (ErrorMessage.ValueKind == JsonValueKind.Array)
                    return string.Join("; ", ErrorMessage.EnumerateArray().Select(e => e.GetString()));

                return ErrorMessage.ToString();
            }

            public string? GetErrorDetails()
            {
                if (ErrorDetails.ValueKind == JsonValueKind.Undefined ||
                    ErrorDetails.ValueKind == JsonValueKind.Null)
                    return null;

                if (ErrorDetails.ValueKind == JsonValueKind.String)
                    return ErrorDetails.GetString();

                if (ErrorDetails.ValueKind == JsonValueKind.Array)
                    return string.Join("; ", ErrorDetails.EnumerateArray().Select(e => e.GetString()));

                return ErrorDetails.ToString();
            }
        }

        private class OcrParsedResult
        {
            public string? ParsedText { get; set; }
            public int FileParseExitCode { get; set; }
            public string? ErrorMessage { get; set; }
            public string? ErrorDetails { get; set; }
        }

        #endregion
    }
}

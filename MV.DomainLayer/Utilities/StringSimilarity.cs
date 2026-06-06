using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MV.DomainLayer.Utilities
{
    /// <summary>
    /// Utility class để tính độ tương đồng giữa 2 chuỗi
    /// Sử dụng cho Fuzzy Matching trong Certificate Validation
    /// </summary>
    public static class StringSimilarity
    {
        /// <summary>
        /// Tính độ tương đồng giữa 2 chuỗi sử dụng Levenshtein Distance
        /// Trả về giá trị từ 0.0 (hoàn toàn khác) đến 1.0 (giống hoàn toàn)
        /// </summary>
        public static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target))
                return 1.0;

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0.0;

            // Normalize strings
            source = NormalizeString(source);
            target = NormalizeString(target);

            // Exact match
            if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Contains check (nếu 1 string chứa string kia)
            if (source.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                target.Contains(source, StringComparison.OrdinalIgnoreCase))
            {
                var shorter = source.Length < target.Length ? source : target;
                var longer = source.Length >= target.Length ? source : target;
                return (double)shorter.Length / longer.Length * 0.9; // 90% nếu contains
            }

            // Levenshtein Distance
            var distance = LevenshteinDistance(source.ToLower(), target.ToLower());
            var maxLength = Math.Max(source.Length, target.Length);

            return 1.0 - ((double)distance / maxLength);
        }

        /// <summary>
        /// Tính độ tương đồng tên người (đặc biệt cho tiếng Việt)
        /// So sánh cả có dấu và không dấu để xử lý trường hợp:
        /// "Dương Thành Phát" ≈ "Thanh Phat Duong"
        /// Threshold mặc định: 70%
        /// </summary>
        public static (bool isMatch, double similarity) CompareNames(string name1, string name2, double threshold = 0.7)
        {
            if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
                return (false, 0);

            // Normalize Vietnamese names
            var normalized1 = NormalizeVietnameseName(name1);
            var normalized2 = NormalizeVietnameseName(name2);

            // Exact match after normalization
            if (normalized1.Equals(normalized2, StringComparison.OrdinalIgnoreCase))
                return (true, 1.0);

            // Tính similarity dựa trên cả có dấu và không dấu
            var similarity1 = CalculateNameSimilarity(normalized1, normalized2);

            // So sánh không dấu (xử lý "Dương Thành Phát" vs "Thanh Phat Duong")
            var noDiacritics1 = RemoveVietnameseDiacritics(normalized1);
            var noDiacritics2 = RemoveVietnameseDiacritics(normalized2);
            var similarity2 = CalculateNameSimilarity(noDiacritics1, noDiacritics2);

            // Lấy giá trị cao nhất
            var bestSimilarity = Math.Max(similarity1, similarity2);

            return (bestSimilarity >= threshold, bestSimilarity);
        }

        /// <summary>
        /// Tính độ tương đồng giữa 2 tên dựa trên các từ thành phần
        /// </summary>
        private static double CalculateNameSimilarity(string name1, string name2)
        {
            var words1 = name1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words2 = name2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words1.Length == 0 || words2.Length == 0)
                return 0;

            // Tính tổng similarity thay vì chỉ đếm match
            double totalSimilarity = 0;
            var usedIndices = new HashSet<int>();

            foreach (var word1 in words1)
            {
                var bestMatchIndex = -1;
                var bestMatchScore = 0.0;

                for (var i = 0; i < words2.Length; i++)
                {
                    if (usedIndices.Contains(i)) continue;

                    var score = word1.Equals(words2[i], StringComparison.OrdinalIgnoreCase)
                        ? 1.0
                        : CalculateSimilarity(word1, words2[i]);

                    if (score > bestMatchScore)
                    {
                        bestMatchScore = score;
                        bestMatchIndex = i;
                    }
                }

                // Thêm score tốt nhất vào tổng (thay vì chỉ đếm match >= 80%)
                if (bestMatchIndex >= 0)
                {
                    totalSimilarity += bestMatchScore;
                    usedIndices.Add(bestMatchIndex);
                }
            }

            // Tính trung bình similarity của tất cả các từ
            var totalWords = Math.Max(words1.Length, words2.Length);
            return totalSimilarity / totalWords;
        }

        /// <summary>
        /// Normalize string chung
        /// </summary>
        private static string NormalizeString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Loại bỏ khoảng trắng thừa
            var normalized = Regex.Replace(input.Trim(), @"\s+", " ");

            // Loại bỏ các ký tự đặc biệt không cần thiết
            normalized = Regex.Replace(normalized, @"[^\w\s\u00C0-\u024F]", "");

            return normalized;
        }

        /// <summary>
        /// Normalize tên tiếng Việt
        /// </summary>
        private static string NormalizeVietnameseName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            // Trim và chuẩn hóa khoảng trắng
            var normalized = Regex.Replace(name.Trim(), @"\s+", " ");

            // Chuyển về dạng title case
            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            normalized = textInfo.ToTitleCase(normalized.ToLower());

            return normalized;
        }

        /// <summary>
        /// Tính Levenshtein Distance giữa 2 string
        /// </summary>
        private static int LevenshteinDistance(string source, string target)
        {
            var sourceLength = source.Length;
            var targetLength = target.Length;

            var distance = new int[sourceLength + 1, targetLength + 1];

            for (var i = 0; i <= sourceLength; i++)
                distance[i, 0] = i;

            for (var j = 0; j <= targetLength; j++)
                distance[0, j] = j;

            for (var i = 1; i <= sourceLength; i++)
            {
                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
        }

        /// <summary>
        /// Loại bỏ dấu tiếng Việt (dùng khi cần so sánh không phân biệt dấu)
        /// </summary>
        public static string RemoveVietnameseDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            // Xử lý đặc biệt cho Đ/đ
            return sb.ToString()
                .Replace("Đ", "D")
                .Replace("đ", "d")
                .Normalize(NormalizationForm.FormC);
        }
    }
}

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request đơn giản cho login (không qua Supabase)
    /// </summary>
    public class SimpleLoginRequest
    {
        /// <summary>
        /// Email hoặc số điện thoại
        /// </summary>
        public string EmailOrPhone { get; set; } = string.Empty;

        /// <summary>
        /// Mật khẩu
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}

namespace MV.DomainLayer.Constants;

public static class ConfigurationKeys
{
    public static class ConnectionStrings
    {
        public const string DefaultConnection = "DefaultConnection";
        public const string RedisConnection = "RedisConnection";
        public const string SupabaseConnection = "SupabaseConnection";
    }

    public static class Kafka
    {
        public const string SectionName = "Kafka";
        public const string BootstrapServers = "BootstrapServers";
    }

    public static class Jwt
    {
        public const string Key = "Jwt:Key";
        public const string Issuer = "Jwt:Issuer";
        public const string Audience = "Jwt:Audience";
        public const string RefreshTokenExpiryDays = "Jwt:RefreshTokenExpiryDays";
    }

    public static class Supabase
    {
        public const string BaseUrl = "Supabase:BaseUrl";
        public const string JwtSecret = "Supabase:JwtSecret";
        public const string ServiceRoleKey = "Supabase:ServiceRoleKey";
        public const string AuthV1Path = "/auth/v1";
        public const string JwksPath = "/.well-known/jwks.json";
    }

    public static class ZaloOA
    {
        public const string SectionName = "ZaloOA";
        public const string AppId = "ZaloOA:AppId";
        public const string SecretKey = "ZaloOA:SecretKey";
        public const string AppSecretKey = "ZaloOA:AppSecretKey";
    }

    public static class ZaloPay
    {
        public const string AppId = "ZaloPay:AppId";
        public const string Key1 = "ZaloPay:Key1";
        public const string Key2 = "ZaloPay:Key2";
    }
}

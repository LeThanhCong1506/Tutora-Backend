using MV.DomainLayer.Enums;

namespace MV.DomainLayer.Helpers
{
    public static class GenderHelper
    {
        /// <summary>Maps FPT AI eKYC "sex" strings (NAM/NỮ) to Gender enum.</summary>
        public static Gender? FromEkycSex(string? sex) =>
            string.IsNullOrWhiteSpace(sex) ? null
            : sex.Trim().ToUpperInvariant() switch
            {
                "NAM" => Gender.Male,
                "NỮ" or "NU" => Gender.Female,
                _ => Gender.Other
            };
    }
}

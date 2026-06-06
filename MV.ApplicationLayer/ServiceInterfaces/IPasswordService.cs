namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IPasswordService
    {
        /// <summary>
        /// Change a user's password after verifying the current (old) password.
        /// </summary>
        Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(string userId, string oldPassword, string newPassword);
    }
}

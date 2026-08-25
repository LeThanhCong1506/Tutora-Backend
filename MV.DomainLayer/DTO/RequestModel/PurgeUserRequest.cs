using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Body of the permanent-erase call. The phrase is typed by the operator and re-checked against a
/// server-generated one, so a client cannot shortcut the confirmation dialog.
/// </summary>
public class PurgeUserRequest
{
    [Required(ErrorMessage = "Vui lòng nhập câu xác nhận.")]
    public string ConfirmationPhrase { get; set; } = null!;
}

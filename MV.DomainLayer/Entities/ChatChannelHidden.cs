namespace MV.DomainLayer.Entities;

/// <summary>
/// Một người đã ẩn ("xoá phía tôi") một kênh chat. Kênh và tin nhắn vẫn còn
/// nguyên cho phía bên kia; kênh hiện lại khi có tin mới sau <see cref="Hiddenat"/>.
/// </summary>
public partial class ChatChannelHidden
{
    public int Channelid { get; set; }

    public string Userid { get; set; } = null!;

    public DateTime Hiddenat { get; set; }

    public virtual Chatchannel Channel { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

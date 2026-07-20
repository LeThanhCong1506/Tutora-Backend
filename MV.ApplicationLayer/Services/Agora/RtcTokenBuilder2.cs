namespace MV.ApplicationLayer.Services.Agora;

/// <summary>
/// Builder tạo Agora RTC token (AccessToken2, version "007") — tương đương
/// RtcTokenBuilder2 trong bộ AgoraDynamicKey chính thức của Agora.
///
/// tokenExpire / privilegeExpire là số giây TƯƠNG ĐỐI kể từ issueTs
/// (server Agora tự cộng issueTs + expire để ra thời điểm hết hạn tuyệt đối).
/// </summary>
internal static class RtcTokenBuilder2
{
    /// <summary>Người dùng có quyền publish audio/video (host).</summary>
    public const int RolePublisher = 1;

    /// <summary>Người dùng chỉ subscribe (audience) — chỉ có quyền join channel.</summary>
    public const int RoleSubscriber = 2;

    /// <summary>
    /// Tạo token gắn với một Agora user account (string).
    /// </summary>
    public static string BuildTokenWithUserAccount(
        string appId,
        string appCertificate,
        string channelName,
        string account,
        int role,
        uint tokenExpire,
        uint privilegeExpire,
        uint issueTs,
        uint salt)
    {
        var token = new AccessToken2(appId, appCertificate, issueTs, tokenExpire, salt);
        var service = new ServiceRtc(channelName, account);

        service.AddPrivilege(ServiceRtc.PrivilegeJoinChannel, privilegeExpire);
        if (role == RolePublisher)
        {
            service.AddPrivilege(ServiceRtc.PrivilegePublishAudioStream, privilegeExpire);
            service.AddPrivilege(ServiceRtc.PrivilegePublishVideoStream, privilegeExpire);
            service.AddPrivilege(ServiceRtc.PrivilegePublishDataStream, privilegeExpire);
        }

        token.AddService(service);

        if (!string.IsNullOrEmpty(account))
        {
            var rtmService = new ServiceRtm(account);
            rtmService.AddPrivilege(ServiceRtm.PrivilegeLogin, privilegeExpire);
            token.AddService(rtmService);
        }

        return token.Build();
    }

    /// <summary>
    /// Tạo token gắn với một Agora uid (uint). uid = 0 nghĩa là token dùng chung cho mọi uid.
    /// </summary>
    public static string BuildTokenWithUid(
        string appId,
        string appCertificate,
        string channelName,
        uint uid,
        int role,
        uint tokenExpire,
        uint privilegeExpire,
        uint issueTs,
        uint salt)
    {
        string account = uid == 0 ? string.Empty : uid.ToString();
        return BuildTokenWithUserAccount(
            appId, appCertificate, channelName, account, role, tokenExpire, privilegeExpire, issueTs, salt);
    }
}

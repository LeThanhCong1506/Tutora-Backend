namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IZaloChatbotService
{
    /// <summary>
    /// Process an inbound text message from a Zalo user; drives the chatbot state machine
    /// (IDLE → ASK_SUBJECT → ASK_GRADE → ASK_AREA → results) and sends a reply via Zalo OA.
    /// </summary>
    Task HandleUserMessageAsync(string senderZaloId, string messageText);
}

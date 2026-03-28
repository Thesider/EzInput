namespace Controller.Interface;

public interface IHtmlSanitizationService
{
    string SanitizeRichText(string html);
    string SanitizePlainText(string input);
}

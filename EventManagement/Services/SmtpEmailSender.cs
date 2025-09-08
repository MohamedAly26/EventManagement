using System.Net;
using System.Net.Mail;
using EventManagement.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EventManagement.Services;


public class SmtpEmailSender(IOptions<SmtpOptions> opt, ILogger<SmtpEmailSender> log) : IEmailSender
{
    private readonly SmtpOptions _o = opt.Value;
    private readonly ILogger<SmtpEmailSender> _log = log;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var from = ParseFrom(_o.From ?? _o.User);

        using var msg = new MailMessage
        {
            From = from,
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true
        };
        msg.To.Add(email);

        using var client = new SmtpClient(_o.Host, _o.Port)
        {
            EnableSsl = _o.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_o.User, _o.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        try
        {
            await client.SendMailAsync(msg);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SMTP send failed: Host={Host}, User={User}", _o.Host, _o.User);
            throw; // fallo emergere: utile in /diag/smtp
        }
    }

    private static MailAddress ParseFrom(string from)
    {
        if (from.Contains("<") && from.Contains(">"))
        {
            var name = from[..from.IndexOf('<')].Trim();
            var email = from[(from.IndexOf('<') + 1)..from.IndexOf('>')].Trim();
            return new MailAddress(email, name);
        }
        return new MailAddress(from);
    }
}

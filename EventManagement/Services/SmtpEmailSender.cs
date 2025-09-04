using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EventManagement.Services
{
    public class SmtpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
        public string From { get; set; } = "noreply@example.com";
        public string FromName { get; set; } = "EventManagement";
    }

    // Implements Identity's IEmailSender
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _o;

        public SmtpEmailSender(IOptions<SmtpOptions> options)
        {
            _o = options.Value;
        }

        // Identity calls this
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
            => SendAsync(email, subject, htmlMessage);

        // Internal helper
        private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            using var client = new SmtpClient(_o.Host, _o.Port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_o.User, _o.Pass)
            };

            using var msg = new MailMessage
            {
                From = new MailAddress(_o.From, _o.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(toEmail);

            await client.SendMailAsync(msg, ct);
        }
    }
}

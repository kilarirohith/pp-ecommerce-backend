using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Ecommerce.Api.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;

    public SmtpEmailSender(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var host     = _config["Smtp:Host"];
        var portText = _config["Smtp:Port"] ?? "587";
        var username = _config["Smtp:Username"];
        var password = _config["Smtp:Password"];
        var from     = _config["Smtp:From"];

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("SMTP configuration is missing or incomplete.");
        }

        var port = int.Parse(portText);

        using var client = new SmtpClient(host, port)
        {
            UseDefaultCredentials = false,                         // 👈 important
            Credentials = new NetworkCredential(username, password),
            EnableSsl = true                                      // 👈 required for most providers
        };

        var mail = new MailMessage(from, toEmail, subject, body)
        {
            IsBodyHtml = true
        };

        await client.SendMailAsync(mail);
    }
}

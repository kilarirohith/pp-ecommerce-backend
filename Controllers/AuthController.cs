using System.Security.Cryptography;
using System.Text;
using Ecommerce.Api.Data;
using Ecommerce.Api.Dtos;
using Ecommerce.Api.Models;
using Ecommerce.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IJwtService _jwtService;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public AuthController(
            AppDbContext db,
            IJwtService jwtService,
            IEmailSender emailSender,
            IConfiguration config)
        {
            _db = db;
            _jwtService = jwtService;
            _emailSender = emailSender;
            _config = config;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
        {
            if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            {
                return BadRequest("Email already registered.");
            }

            using var hmac = new HMACSHA256();
            var salt = hmac.Key;
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(request.Password));

            var user = new User
            {
                FullName = request.FullName,
                Email = request.Email,
                PasswordSalt = salt,
                PasswordHash = hash,
                Role = "User",
                Permissions = new UserPermissions
                {
                    CanManageProducts = false,
                    CanViewAdminOrders = false,
                    CanManageUsers = false
                }
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var token = _jwtService.GenerateToken(user);

            return new AuthResponse
            {
                Token = token,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role
            };
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            var user = await _db.Users
                .Include(u => u.Permissions)
                .FirstOrDefaultAsync(u => u.Email == request.Email);

            if (user == null) return Unauthorized("Invalid credentials.");

            using var hmac = new HMACSHA256(user.PasswordSalt);
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(request.Password));

            if (!computedHash.SequenceEqual(user.PasswordHash))
                return Unauthorized("Invalid credentials.");

            var token = _jwtService.GenerateToken(user);

            return new AuthResponse
            {
                Token = token,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role
            };
        }

        // ============= FORGOT PASSWORD (SEND OTP TO EMAIL) ============
        [HttpPost("forgot")]
        [AllowAnonymous]
        public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

            // Always respond OK to avoid leaking which emails exist
            if (user == null)
            {
                return Ok(new { message = "If that email exists, an OTP has been sent." });
            }

            // Generate 6-digit OTP
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var code = BitConverter.ToUInt32(bytes, 0) % 1000000;
            var otp = code.ToString("D6"); // 000000 - 999999

            user.PasswordResetToken = otp;
            user.PasswordResetExpiry = DateTime.UtcNow.AddMinutes(10);
            await _db.SaveChangesAsync();

            var frontendBaseUrl = _config["Smtp:FrontendBaseUrl"] ?? "http://localhost:4200";
            var resetUrl = $"{frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}";

            var subject = "Your Password Reset OTP";
            var body = $@"
                <p>Hi {user.FullName},</p>
                <p>Your OTP for resetting your password is:</p>
                <h2>{otp}</h2>
                <p>This code will expire in 10 minutes.</p>
                <p>You can reset your password here:</p>
                <p><a href=""{resetUrl}"">{resetUrl}</a></p>
                <p>If you did not request this, you can ignore this email.</p>
            ";

            await _emailSender.SendEmailAsync(user.Email, subject, body);

            return Ok(new { message = "If that email exists, an OTP has been sent." });
        }

        // ============= RESET PASSWORD WITH OTP ============
        [HttpPost("reset")]
        [AllowAnonymous]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
            {
                return BadRequest("Invalid OTP or email.");
            }

            if (user.PasswordResetToken == null ||
                user.PasswordResetExpiry == null ||
                user.PasswordResetExpiry < DateTime.UtcNow ||
                !string.Equals(user.PasswordResetToken, request.Token, StringComparison.Ordinal))
            {
                return BadRequest("Invalid or expired OTP.");
            }

            // Set new password
            using var hmac = new HMACSHA256();
            user.PasswordSalt = hmac.Key;
            user.PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(request.NewPassword));

            // Clear OTP
            user.PasswordResetToken = null;
            user.PasswordResetExpiry = null;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Password reset successfully." });
        }
    }
}

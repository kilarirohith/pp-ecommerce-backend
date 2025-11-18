using System.ComponentModel.DataAnnotations;

namespace Ecommerce.Api.Dtos;

public class RegisterRequest
{
    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = "";

    [Required]
    [MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required]
    [MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public class AuthResponse
{
    public string Token { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
}

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";
}

public class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Token { get; set; } = ""; 

    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = "";
}

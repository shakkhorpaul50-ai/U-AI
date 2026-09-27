using System.ComponentModel.DataAnnotations;

namespace UAI.Models;

public sealed class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, StringLength(72, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [DataType(DataType.Password), Compare("Password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed record SessionSummary(long Id, string Title, string Mode, DateTimeOffset UpdatedAt);

public sealed record TurnDto(long Id, string Role, string Content);

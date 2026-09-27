using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UAI.Data;
using UAI.Models;

namespace UAI.Controllers;

[AllowAnonymous]
public sealed class AccountController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IConfiguration config) : Controller
{
    /// <summary>
    /// Optional invite gate. Set UAI__InviteCode (env: UAI__InviteCode or INVITE_CODE
    /// via config) to require it. When unset, registration stays open for local dev.
    /// </summary>
    private string? RequiredInvite =>
        config["UAI:InviteCode"] ?? Environment.GetEnvironmentVariable("INVITE_CODE");

    private bool InviteRequired => !string.IsNullOrWhiteSpace(RequiredInvite);

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login(LoginViewModel m)
    {
        if (!ModelState.IsValid) return View(m);

        try
        {
            var result = await signIn.PasswordSignInAsync(
                m.Email, m.Password, isPersistent: m.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
                return Redirect(Safe(m.ReturnUrl));
        }
        catch (Exception)
        {
            ModelState.AddModelError("", "Sign-in is temporarily unavailable. Try again shortly.");
            return View(m);
        }

        ModelState.AddModelError("", "Incorrect email or password.");
        return View(m);
    }

    [HttpGet]
    public IActionResult Register()
    {
        ViewBag.InviteRequired = InviteRequired;
        return View(new RegisterViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("auth-register")]
    public async Task<IActionResult> Register(RegisterViewModel m)
    {
        ViewBag.InviteRequired = InviteRequired;

        if (InviteRequired &&
            !string.Equals(m.InviteCode?.Trim(), RequiredInvite, StringComparison.Ordinal))
        {
            ModelState.AddModelError("InviteCode", "A valid invite code is required.");
            return View(m);
        }

        if (!ModelState.IsValid) return View(m);

        var user = new AppUser { UserName = m.Email, Email = m.Email, EmailConfirmed = true };

        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, m.Password);
        }
        catch (Exception)
        {
            ModelState.AddModelError("", "Sign-up is temporarily unavailable. Try again shortly.");
            return View(m);
        }

        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError("", e.Description);
            return View(m);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return RedirectToAction("Index", "Chat");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private string Safe(string? url) =>
        !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : Url.Action("Index", "Chat")!;
}

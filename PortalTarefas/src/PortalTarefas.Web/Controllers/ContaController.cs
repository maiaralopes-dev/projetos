using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace PortalTarefas.Web.Controllers;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress, Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe a senha."), DataType(DataType.Password), Display(Name = "Senha")]
    public string Senha { get; set; } = "";
}

/// <summary>Login/logout da interface MVC (mesmo cookie de sessão usado pela SPA).</summary>
public sealed class ContaController(SignInManager<IdentityUser> signIn) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl) { ViewData["ReturnUrl"] = returnUrl; return View(new LoginViewModel()); }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel m, string? returnUrl)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(m);
        var usuario = await signIn.UserManager.FindByEmailAsync(m.Email);
        var r = usuario is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed : await signIn.PasswordSignInAsync(usuario, m.Senha, false, lockoutOnFailure: true);
        if (!r.Succeeded)
        {
            ModelState.AddModelError("", "E-mail ou senha inválidos, ou acesso temporariamente bloqueado.");
            return View(m);
        }
        // LocalRedirect impede redirecionamento aberto para outro site.
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Tarefas");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        if (await signIn.UserManager.GetUserAsync(User) is { } u) await signIn.UserManager.UpdateSecurityStampAsync(u);
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AcessoNegado() { Response.StatusCode = 403; return View(); }
}

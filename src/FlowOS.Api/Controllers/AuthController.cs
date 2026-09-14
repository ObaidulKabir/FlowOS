using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ITenantAuthService _authService;

    public AuthController(ITenantAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Registers a new tenant organization and primary administrator account with email verification.
    /// </summary>
    [HttpPost("register-tenant")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantUserRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterTenantAsync(request, ct);
        if (!result.Success)
        {
            if (result.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                result.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { ok = false, message = result.Message });
            }

            return BadRequest(new { ok = false, message = result.Message });
        }

        return StatusCode(201, new
        {
            ok = true,
            message = result.Message,
            tenantId = result.TenantId,
            tenantName = result.TenantName,
            email = result.Email,
            isEmailVerified = result.IsEmailVerified,
            verificationToken = result.VerificationToken
        });
    }

    /// <summary>
    /// Verifies tenant administrator email address using the supplied token.
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        var result = await _authService.VerifyEmailAsync(request.Email, request.Token, ct);
        if (!result.Success)
        {
            return BadRequest(new { ok = false, message = result.Message });
        }

        return Ok(new
        {
            ok = true,
            message = result.Message,
            tenantId = result.TenantId,
            email = result.Email,
            isEmailVerified = result.IsEmailVerified
        });
    }

    /// <summary>
    /// Web browser link endpoint for email verification.
    /// </summary>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmailGet([FromQuery] string email, [FromQuery] string token, CancellationToken ct)
    {
        var result = await _authService.VerifyEmailAsync(email, token, ct);
        if (!result.Success)
        {
            return BadRequest(new { ok = false, message = result.Message });
        }

        if (Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return Content(
                "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Email Verified - FlowOS</title></head>" +
                "<body style='font-family: sans-serif; text-align: center; padding: 50px; background: #0f172a; color: #f8fafc;'>" +
                "<h1 style='color: #38bdf8;'>Email Verified Successfully!</h1>" +
                "<p style='color: #94a3b8; font-size: 18px;'>Your FlowOS tenant account is now active. You may now log in to the FlowOS portal.</p>" +
                "</body></html>",
                "text/html");
        }

        return Ok(new
        {
            ok = true,
            message = result.Message,
            tenantId = result.TenantId,
            email = result.Email,
            isEmailVerified = result.IsEmailVerified
        });
    }

    /// <summary>
    /// Authenticates a tenant administrator and issues a signed JWT token.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password, ct);
        if (!result.Success)
        {
            if (result.ErrorCode == "EMAIL_NOT_VERIFIED")
            {
                return StatusCode(403, new
                {
                    ok = false,
                    errorCode = result.ErrorCode,
                    message = result.Message
                });
            }

            return Unauthorized(new
            {
                ok = false,
                errorCode = result.ErrorCode ?? "INVALID_CREDENTIALS",
                message = result.Message
            });
        }

        return Ok(new
        {
            ok = true,
            token = result.Token,
            tokenType = result.TokenType,
            expiresIn = result.ExpiresIn,
            user = result.User
        });
    }

    /// <summary>
    /// Re-sends a verification email to the user.
    /// </summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        var result = await _authService.ResendVerificationEmailAsync(request.Email, ct);
        if (!result.Success)
        {
            return BadRequest(new { ok = false, message = result.Message });
        }

        return Ok(new
        {
            ok = true,
            message = result.Message,
            verificationToken = result.VerificationToken
        });
    }

    /// <summary>
    /// Returns the currently authenticated user and tenant profile.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subClaim) || !Guid.TryParse(subClaim, out var userId))
        {
            return Unauthorized(new { ok = false, message = "User is not authenticated with a valid tenant identity." });
        }

        var user = await _authService.GetCurrentUserAsync(userId, ct);
        if (user == null)
        {
            return NotFound(new { ok = false, message = "User not found." });
        }

        return Ok(new { ok = true, user });
    }
}

public record ResendVerificationRequest(string Email);

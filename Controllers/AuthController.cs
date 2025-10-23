using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.ComponentModel.DataAnnotations;
using BookInventory.Api.Models;
using BookInventory.Api.Data;
using MongoDB.Driver;
using Microsoft.AspNetCore.Authorization;

namespace BookInventory.Api.Controllers
{
    // DTOs - Create these in a separate DTOs folder
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(2)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MinLength(2)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, and one number")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public UserInfo User { get; set; } = null!;
    }

    public class UserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IMongoCollection<AppUser> _users;
        private readonly ILogger<AuthController> _logger;
        // Cookie name constant
        private const string AuthCookieName = "auth_token";

        public AuthController(
            IConfiguration config,
            MongoDbContext context,
            ILogger<AuthController> logger)
        {
            _config = config;
            _users = context.Users;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterDto registerDto)
        {
            // Validate model state
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Normalize email for consistent lookups
                var normalizedEmail = registerDto.Email.ToLowerInvariant().Trim();

                // Check if user exists
                var existing = await _users.Find(u => u.Email == normalizedEmail)
                    .FirstOrDefaultAsync();

                if (existing != null)
                    return BadRequest(new { message = "Email already exists" });

                // Create user
                var user = new AppUser
                {
                    Email = normalizedEmail,
                    FirstName = registerDto.FirstName.Trim(),
                    LastName = registerDto.LastName.Trim(),
                    Password = BCrypt.Net.BCrypt.HashPassword(registerDto.Password),
                    CreatedAt = DateTime.UtcNow
                };

                await _users.InsertOneAsync(user);

                // Validate user ID was generated
                if (string.IsNullOrEmpty(user.Id))
                {
                    throw new InvalidOperationException("Failed to generate user ID");
                }

                // Generate token
                var (token, expiresAt) = GenerateJwtToken(user);

                SetAuthCookie(token, expiresAt);


                return Ok(new AuthResponse
                {
                    User = new UserInfo
                    {
                        Id = user.Id,
                        Email = user.Email,
                        FirstName = user.FirstName,
                        LastName = user.LastName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration");
                return StatusCode(500, new { message = "An error occurred during registration" });
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var normalizedEmail = loginDto.Email.ToLowerInvariant().Trim();

                var user = await _users.Find(u => u.Email == normalizedEmail)
                    .FirstOrDefaultAsync();

                if (user == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.Password))
                {
                    // Don't reveal whether email exists
                    return Unauthorized(new { message = "Invalid credentials" });
                }

                var (token, expiresAt) = GenerateJwtToken(user);

                // Set auth cookie for login flow as well
                SetAuthCookie(token, expiresAt);

                return Ok(new AuthResponse
                {
                    User = new UserInfo
                    {
                        Id = user.Id ?? "",
                        Email = user.Email,
                        FirstName = user.FirstName,
                        LastName = user.LastName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user login");
                return StatusCode(500, new { message = "An error occurred during login" });
            }
        }

        [HttpPost("refresh")]
        [Authorize]
        public async Task<ActionResult<AuthResponse>> RefreshToken()
        {
            try
            {
                // Get user ID from current token claims
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Refresh token failed - no user ID in claims");
                    return Unauthorized(new { message = "Invalid token" });
                }

                // Look up user in database
                var user = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync();

                if (user == null)
                {
                    _logger.LogWarning("Refresh token failed - user {UserId} not found", userId);
                    return Unauthorized(new { message = "User not found" });
                }

                // Generate new token with fresh expiration
                var (token, expiresAt) = GenerateJwtToken(user);
                SetAuthCookie(token, expiresAt);

                _logger.LogInformation("Token refreshed successfully for user {UserId}", userId);

                return Ok(new AuthResponse
                {
                    User = new UserInfo
                    {
                        Id = user.Id ?? "",
                        Email = user.Email,
                        FirstName = user.FirstName,
                        LastName = user.LastName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return StatusCode(500, new { message = "An error occurred during token refresh" });
            }
        }

        private (string token, DateTime expiresAt) GenerateJwtToken(AppUser user)
        {
            var jwtSettings = _config.GetSection("Jwt");

            // Validate configuration
            var key = jwtSettings["Key"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];
            var expiresInMinutes = jwtSettings["ExpiresInMinutes"];

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(issuer) ||
                string.IsNullOrEmpty(audience) || string.IsNullOrEmpty(expiresInMinutes))
            {
                throw new InvalidOperationException("JWT configuration is incomplete");
            }

            if (key.Length < 32)
            {
                throw new InvalidOperationException("JWT key must be at least 32 characters");
            }

            if (!double.TryParse(expiresInMinutes, out var minutes) || minutes <= 0)
            {
                throw new InvalidOperationException("JWT ExpiresInMinutes must be a valid positive number");
            }

            // Validate user ID exists
            if (string.IsNullOrEmpty(user.Id))
            {
                throw new InvalidOperationException("User ID is required for token generation");
            }

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("firstName", user.FirstName),
                new Claim("lastName", user.LastName)
            };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var expiresAt = DateTime.UtcNow.AddMinutes(minutes);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: creds
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Clear the auth cookie
            Response.Cookies.Delete(AuthCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = false, // Use in production with HTTPS
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });

            return Ok(new { message = "Logged out successfully" });
        }

        /// <summary>
        /// Sets the JWT token as an httpOnly cookie
        /// </summary>
        private void SetAuthCookie(string token, DateTime expiresAt)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,        // Cannot be accessed by JavaScript
                Secure = false,          // Only sent over HTTPS (set to false for local dev if needed)
                SameSite = SameSiteMode.Strict,  // CSRF protection
                Expires = expiresAt,    // Cookie expires when token expires
                Path = "/"              // Available for all paths
            };

            Response.Cookies.Append(AuthCookieName, token, cookieOptions);
        }
    }
}
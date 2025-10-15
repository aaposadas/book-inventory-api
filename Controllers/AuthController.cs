using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.ComponentModel.DataAnnotations;
using BookInventory.Api.Models;
using BookInventory.Api.Data;
using MongoDB.Driver;

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
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
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

                return Ok(new AuthResponse
                {
                    Token = token,
                    ExpiresAt = expiresAt,
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

                return Ok(new AuthResponse
                {
                    Token = token,
                    ExpiresAt = expiresAt,
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
    }
}
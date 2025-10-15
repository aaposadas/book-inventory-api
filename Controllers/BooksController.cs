using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using BookInventory.Api.Models;
using BookInventory.Api.Data;
using MongoDB.Driver;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace BookInventory.Api.Controllers
{
    // DTOs
    public class AddBookByIsbnDto
    {
        [Required]
        [RegularExpression(@"^(?:\d{10}|\d{13})$", ErrorMessage = "ISBN must be 10 or 13 digits")]
        public string ISBN { get; set; } = string.Empty;
    }

    public class UpdateBookDto
    {
        [Required]
        [MinLength(1)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MinLength(1)]
        public string Author { get; set; } = string.Empty;

        public string? PublishedDate { get; set; }
        public string? Description { get; set; }
        public List<string> Categories { get; set; } = new();
        public string? CoverUrl { get; set; }
        public string? ISBN { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BooksController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _googleBooksApiKey;
        private readonly IMongoCollection<Book> _books;
        private readonly ILogger<BooksController> _logger;

        public BooksController(
            HttpClient httpClient,
            IConfiguration config,
            MongoDbContext context,
            ILogger<BooksController> logger)
        {
            _httpClient = httpClient;
            _googleBooksApiKey = config["GoogleBooks:ApiKey"] ?? string.Empty;
            _books = context.Books;
            _logger = logger;
        }

        private string GetUserId()
        {
            // JWT token uses "sub" claim, not NameIdentifier
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("User ID not found in token claims");
                throw new UnauthorizedAccessException("User ID not found in token");
            }

            return userId;
        }

        /// <summary>
        /// Get all books for the authenticated user
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<Book>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<Book>>> GetBooks(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? category = null)
        {
            try
            {
                var userId = GetUserId();

                // Validate pagination
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                // Build filter
                var filterBuilder = Builders<Book>.Filter;
                var filter = filterBuilder.Eq(b => b.UserId, userId);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchFilter = filterBuilder.Or(
                        filterBuilder.Regex(b => b.Title, new MongoDB.Bson.BsonRegularExpression(search, "i")),
                        filterBuilder.Regex(b => b.Author, new MongoDB.Bson.BsonRegularExpression(search, "i"))
                    );
                    filter = filterBuilder.And(filter, searchFilter);
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    filter = filterBuilder.And(filter, filterBuilder.AnyEq(b => b.Categories, category));
                }

                // Get total count for pagination
                var totalCount = await _books.CountDocumentsAsync(filter);

                // Get paginated results
                var books = await _books.Find(filter)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .SortByDescending(b => b.Id)
                    .ToListAsync();

                Response.Headers.Append("X-Total-Count", totalCount.ToString());
                Response.Headers.Append("X-Page", page.ToString());
                Response.Headers.Append("X-Page-Size", pageSize.ToString());

                return Ok(books);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving books for user");
                return StatusCode(500, new { message = "An error occurred while retrieving books" });
            }
        }

        /// <summary>
        /// Add a book by ISBN lookup via Google Books API
        /// </summary>
        [HttpPost("isbn/{isbn}")]
        [ProducesResponseType(typeof(Book), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<Book>> AddBookByIsbn(string isbn)
        {
            try
            {
                // Validate ISBN format
                var cleanIsbn = isbn.Replace("-", "").Replace(" ", "");
                if (!System.Text.RegularExpressions.Regex.IsMatch(cleanIsbn, @"^(?:\d{10}|\d{13})$"))
                {
                    return BadRequest(new { message = "ISBN must be 10 or 13 digits" });
                }

                var userId = GetUserId();

                // Check if book already exists for this user
                var existingBook = await _books.Find(b => b.UserId == userId && b.ISBN == cleanIsbn)
                    .FirstOrDefaultAsync();

                if (existingBook != null)
                {
                    return Conflict(new { message = "This book is already in your collection", book = existingBook });
                }

                // Validate API key
                if (string.IsNullOrEmpty(_googleBooksApiKey))
                {
                    _logger.LogError("Google Books API key is not configured");
                    return StatusCode(500, new { message = "Book lookup service is not configured" });
                }

                // Google Books API lookup
                var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{cleanIsbn}&key={_googleBooksApiKey}";

                GoogleBooksResponse? response;
                try
                {
                    response = await _httpClient.GetFromJsonAsync<GoogleBooksResponse>(url);
                }
                catch (HttpRequestException)
                {
                    _logger.LogError("Error calling Google Books API for ISBN: {ISBN}", cleanIsbn);
                    return StatusCode(503, new { message = "Book lookup service is temporarily unavailable" });
                }

                if (response == null || response.Items == null || !response.Items.Any())
                {
                    return NotFound(new { message = "Book not found in Google Books database" });
                }

                var volumeInfo = response.Items.First().VolumeInfo;
                if (volumeInfo == null)
                {
                    return NotFound(new { message = "Book information incomplete in database" });
                }

                var book = new Book
                {
                    ISBN = cleanIsbn,
                    Title = volumeInfo.Title ?? "Unknown Title",
                    Author = volumeInfo.Authors?.FirstOrDefault() ?? "Unknown Author",
                    PublishedDate = volumeInfo.PublishedDate,
                    Description = volumeInfo.Description,
                    Categories = volumeInfo.Categories?.ToList() ?? new List<string>(),
                    CoverUrl = volumeInfo.ImageLinks?.Thumbnail?.Replace("http://", "https://"), // Force HTTPS
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                await _books.InsertOneAsync(book);

                _logger.LogInformation("Book added successfully. ISBN: {ISBN}, UserId: {UserId}", cleanIsbn, userId);

                return Ok(book);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding book by ISBN");
                return StatusCode(500, new { message = "An error occurred while adding the book" });
            }
        }

        /// <summary>
        /// Get a specific book by ID
        /// </summary>
        [HttpGet("{id:length(24)}")]
        [ProducesResponseType(typeof(Book), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Book>> GetBook(string id)
        {
            try
            {
                var userId = GetUserId();

                var book = await _books.Find(b => b.Id == id && b.UserId == userId)
                    .FirstOrDefaultAsync();

                if (book == null)
                {
                    return NotFound(new { message = "Book not found" });
                }

                return Ok(book);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving book {BookId}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the book" });
            }
        }

        /// <summary>
        /// Update a book
        /// </summary>
        [HttpPut("{id:length(24)}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateBook(string id, [FromBody] UpdateBookDto updateDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var userId = GetUserId();

                var existingBook = await _books.Find(b => b.Id == id && b.UserId == userId)
                    .FirstOrDefaultAsync();

                if (existingBook == null)
                {
                    return NotFound(new { message = "Book not found" });
                }

                // Update only the fields that should be editable
                var update = Builders<Book>.Update
                    .Set(b => b.Title, updateDto.Title)
                    .Set(b => b.Author, updateDto.Author)
                    .Set(b => b.PublishedDate, updateDto.PublishedDate)
                    .Set(b => b.Description, updateDto.Description)
                    .Set(b => b.Categories, updateDto.Categories)
                    .Set(b => b.CoverUrl, updateDto.CoverUrl)
                    .Set(b => b.ISBN, updateDto.ISBN);

                await _books.UpdateOneAsync(b => b.Id == id && b.UserId == userId, update);

                _logger.LogInformation("Book updated successfully. BookId: {BookId}, UserId: {UserId}", id, userId);

                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating book {BookId}", id);
                return StatusCode(500, new { message = "An error occurred while updating the book" });
            }
        }

        /// <summary>
        /// Delete a book
        /// </summary>
        [HttpDelete("{id:length(24)}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteBook(string id)
        {
            try
            {
                var userId = GetUserId();

                var result = await _books.DeleteOneAsync(b => b.Id == id && b.UserId == userId);

                if (result.DeletedCount == 0)
                {
                    return NotFound(new { message = "Book not found" });
                }

                _logger.LogInformation("Book deleted successfully. BookId: {BookId}, UserId: {UserId}", id, userId);

                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting book {BookId}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the book" });
            }
        }
    }
}
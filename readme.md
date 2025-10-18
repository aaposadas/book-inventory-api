# Book Inventory API

A RESTful API built with ASP.NET Core for managing personal book collections. Users can register, authenticate, and manage their book inventory with automatic book information lookup via the Google Books API.

## 🚀 Features

- **User Authentication**: JWT-based authentication with secure password hashing (BCrypt)
- **Book Management**: Full CRUD operations for personal book collections
- **ISBN Lookup**: Automatic book information retrieval using Google Books API
- **User Isolation**: Each user can only access their own book collection
- **Search & Filter**: Search books by title/author and filter by category
- **Pagination**: Efficient data loading with customizable page sizes

## 🛠️ Tech Stack

- **Framework**: ASP.NET Core 8.0
- **Database**: MongoDB
- **Authentication**: JWT (JSON Web Tokens)
- **Password Hashing**: BCrypt.Net
- **External API**: Google Books API

## 📋 Prerequisites

- .NET 8.0 SDK or later
- MongoDB (local installation or MongoDB Atlas account)
- Google Books API Key ([Get one here](https://developers.google.com/books/docs/v1/using#APIKey))

## ⚙️ Configuration

### Option 1: Using appsettings.Development.json (Local Development)

1. Copy `appsettings.example.json` to `appsettings.Development.json`
2. Fill in your actual values:

```json
{
  "Jwt": {
    "Key": "your-secret-key-minimum-32-characters-long",
    "Issuer": "BookInventoryApi",
    "Audience": "BookInventoryClient",
    "ExpiresInMinutes": "60"
  },
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "BookInventory"
  },
  "GoogleBooks": {
    "ApiKey": "your-google-books-api-key"
  },
  "AllowedOrigin": "http://localhost:4200"
}
```

### Option 2: Using Environment Variables (Production/Azure)

Set the following environment variables (use double underscores for nested properties):

```bash
Jwt__Key=your-secret-key-minimum-32-characters-long
Jwt__Issuer=BookInventoryApi
Jwt__Audience=BookInventoryClient
Jwt__ExpiresInMinutes=60
MongoDbSettings__ConnectionString=your-mongodb-connection-string
MongoDbSettings__DatabaseName=BookInventory
GoogleBooks__ApiKey=your-google-books-api-key
AllowedOrigin=your-frontend-url
```

## 🚦 Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/aaposadas/BookInventory.Api.git
cd BookInventory.Api
```

### 2. Configure settings

Create `appsettings.Development.json` with your configuration (see Configuration section above)

### 3. Restore dependencies

```bash
dotnet restore
```

### 4. Run the application

```bash
dotnet run
```

The API will be available at `http://localhost:5210`

## 📚 API Endpoints

### Authentication

#### Register
```http
POST /api/auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "password": "SecurePass123"
}
```

#### Login
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePass123"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2025-10-17T21:00:00Z",
  "user": {
    "id": "507f1f77bcf86cd799439011",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe"
  }
}
```

### Books (All require Authentication)

Include the JWT token in the Authorization header:
```
Authorization: Bearer {token}
```

#### Get All Books
```http
GET /api/books?page=1&pageSize=20&search=tolkien&category=Fantasy
```

**Query Parameters:**
- `page` (optional): Page number (default: 1)
- `pageSize` (optional): Items per page (default: 20, max: 100)
- `search` (optional): Search by title or author
- `category` (optional): Filter by category

**Response Headers:**
- `X-Total-Count`: Total number of books
- `X-Page`: Current page
- `X-Page-Size`: Items per page

#### Get Book by ID
```http
GET /api/books/{id}
```

#### Add Book by ISBN
```http
POST /api/books/isbn/9780544003415
```

Automatically fetches book information from Google Books API.

#### Update Book
```http
PUT /api/books/{id}
Content-Type: application/json

{
  "title": "Updated Title",
  "author": "Updated Author",
  "publishedDate": "2024-01-01",
  "description": "Updated description",
  "categories": ["Fiction", "Fantasy"],
  "coverUrl": "https://example.com/cover.jpg",
  "isbn": "9780544003415"
}
```

#### Delete Book
```http
DELETE /api/books/{id}
```

## 🔒 Security Features

- **Password Requirements**: Minimum 8 characters with at least one uppercase, lowercase, and number
- **JWT Validation**: Secure token generation with configurable expiration
- **BCrypt Password Hashing**: Industry-standard password protection
- **User Data Isolation**: Users can only access their own books
- **CORS Configuration**: Configurable allowed origins
- **Input Validation**: Comprehensive validation on all endpoints

## 🗄️ Database Schema

### Users Collection
```javascript
{
  "_id": ObjectId,
  "Email": String (required, unique),
  "FirstName": String (required),
  "LastName": String (required),
  "Password": String (hashed, required),
  "CreatedAt": DateTime (UTC)
}
```

### Books Collection
```javascript
{
  "_id": ObjectId,
  "userId": ObjectId (required),
  "ISBN": String,
  "title": String (required),
  "author": String (required),
  "publishedDate": String,
  "description": String,
  "categories": [String],
  "coverUrl": String,
  "createdAt": DateTime (UTC)
}
```

## 🌐 Deployment

### Azure Web App

1. Create an Azure Web App
2. Configure Application Settings (see Configuration section)
3. Deploy using:
   - GitHub Actions
   - Azure DevOps
   - Visual Studio Publish
   - Azure CLI

### MongoDB Atlas Setup

1. Create a MongoDB Atlas cluster
2. Add connection IP addresses to Network Access
   - For Azure: Add your Web App's outbound IP addresses
   - For development: Add your local IP or use 0.0.0.0/0 (not recommended for production)
3. Create a database user
4. Get your connection string and add to configuration

## 🧪 Testing

Use tools like:
- **Postman**: Import the API endpoints
- **Swagger/OpenAPI**: Available in development mode at `/openapi`
- **cURL**: Command-line testing

Example cURL request:
```bash
curl -X POST http://localhost:5210/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "firstName": "Test",
    "lastName": "User",
    "password": "TestPass123"
  }'
```

## 👤 Author

**Andrew Posadas**
- GitHub: [@aaposadas](https://github.com/aaposadas)

## 🤝 Contributing

This is a portfolio project, but feedback and suggestions are welcome! Feel free to open an issue or submit a pull request.
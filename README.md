# NexaStore

> **Enterprise E-Commerce Order Management API**
> Built with .NET 8 · Clean Architecture · CQRS · Azure

[![Build & Deploy API](https://github.com/RaviDeveloper7/NexaStore/actions/workflows/deploy-api.yml/badge.svg)](https://github.com/RaviDeveloper7/NexaStore/actions/workflows/deploy-api.yml)
[![Build & Deploy Functions](https://github.com/RaviDeveloper7/NexaStore/actions/workflows/deploy-functions.yml/badge.svg)](https://github.com/RaviDeveloper7/NexaStore/actions/workflows/deploy-functions.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## What Is NexaStore?

NexaStore is a production-grade backend API for managing digital storefronts, inventory, and e-commerce workflows. It is intentionally over-engineered for a portfolio context — every layer, pattern, and tool reflects real enterprise .NET engineering decisions with documented reasoning.

The goal is not just a working API. It is a codebase that demonstrates architectural thinking, cloud-native design, and the patterns most commonly discussed in senior .NET interviews.

---

## Architecture Overview

NexaStore follows **Clean Architecture** with strict unidirectional dependency rules. No outer layer can be referenced by an inner layer. Dependencies always point inward.

```
┌─────────────────────────────────────────────────────────────┐
│                        NexaStore.Api                        │  ← HTTP entry point
│                    NexaStore.Functions                       │  ← Background jobs
├──────────────────────────┬──────────────────────────────────┤
│   NexaStore.Identity     │   NexaStore.Infrastructure       │  ← Auth + External services
│   NexaStore.Persistence  │                                  │  ← Data access
├──────────────────────────┴──────────────────────────────────┤
│                    NexaStore.Application                     │  ← Business orchestration
│                       CQRS · MediatR · FluentValidation      │
├─────────────────────────────────────────────────────────────┤
│                      NexaStore.Domain                        │  ← Core business rules
│              Entities · Enums · Events · Exceptions          │
└─────────────────────────────────────────────────────────────┘
```

**Dependency Rule** — never broken:

```
Domain ← Application ← Persistence
                    ← Infrastructure
                    ← Identity
                    ← Api
                    ← Functions
```

---

## Tech Stack

| Technology | Layer | Decision |
|---|---|---|
| **.NET 8** | All | LTS release, industry standard |
| **Clean Architecture** | Solution | Enforced unidirectional dependency rule |
| **CQRS + MediatR** | Application | Commands change state, Queries return data — never mixed |
| **FluentValidation** | Application | Validator-per-command, tested independently |
| **Mapster** | Application | IL-compiled mapping — faster than AutoMapper |
| **EF Core 8** | Persistence | Fluent API configuration, no Data Annotations on domain |
| **Repository + Unit of Work** | Persistence | Decouples Application from EF Core entirely |
| **SQL Server** | Persistence | Production-grade relational store |
| **Azure Service Bus** | Infrastructure | Topics + Subscriptions for event fan-out |
| **Outbox Pattern** | Infrastructure | Guaranteed at-least-once event delivery |
| **Azure Functions v4** | Functions | Isolated worker — TimerTrigger + ServiceBusTrigger |
| **Redis** | Infrastructure | Azure Cache for Redis — catalog caching with TTL |
| **JWT + Refresh Tokens** | Identity | Stateless auth, refresh token rotation |
| **Role-Based Authorization** | Identity | Admin / Customer roles seeded via EF migrations |
| **Application Insights** | API + Functions | Structured telemetry, no Serilog needed |
| **Global Exception Middleware** | API | Domain exceptions → correct HTTP status codes |
| **API Versioning** | API | `/api/v1/` — backwards-compatible evolution |
| **Health Checks** | API | SQL Server + Redis + Service Bus probes |
| **Options Pattern** | All | Strongly-typed configuration, no magic strings |
| **xUnit + Moq** | Tests | Unit tests against mocked interfaces |
| **Testcontainers** | Tests | Integration tests against real SQL Server in Docker |
| **GitHub Actions** | CI/CD | Build → Test → Deploy to Azure App Service + Function App |

---

## Solution Structure

```
NexaStore.sln
│
├── src/
│   ├── NexaStore.Domain                  # Entities, Enums, Events, Exceptions
│   ├── NexaStore.Application             # CQRS, Interfaces, Behaviours, DTOs, Mappings
│   ├── NexaStore.Persistence             # EF Core, Repositories, Migrations, UoW
│   ├── NexaStore.Infrastructure          # Email, Redis Cache, Azure Service Bus
│   ├── NexaStore.Identity                # ASP.NET Core Identity, JWT, AuthService
│   ├── NexaStore.Api                     # Controllers, Middleware, Program.cs
│   └── NexaStore.Functions               # OutboxProcessor, OrderExpiry, OrderPlacedConsumer
│
└── tests/
    ├── NexaStore.Application.UnitTests   # xUnit + Moq — handler tests
    └── NexaStore.Persistence.IntegrationTests  # Testcontainers — repository tests
```

---

## Key Design Decisions

### 1. Outbox Pattern — Guaranteed Event Delivery

The biggest architectural risk in an event-driven system is the **dual-write problem**: you save the Order to the database and then publish an event to Service Bus — what happens if the publish fails?

Without the Outbox Pattern: the Order exists in the DB, but the event is lost forever. The customer never gets a confirmation email.

**NexaStore's solution:**

```
PlaceOrderCommandHandler
 ├── Create Order entity
 ├── Create OutboxMessage entity (JSON-serialised event)
 └── SaveChangesAsync()                        ← ONE atomic DB transaction
          ↓
OutboxProcessorFunction  (TimerTrigger — every 10s)
 ├── SELECT TOP 50 * FROM OutboxMessages WHERE ProcessedAt IS NULL
 ├── Publish each to Azure Service Bus topic
 └── UPDATE OutboxMessages SET ProcessedAt = UtcNow WHERE Id = @id
```

Order and OutboxMessage are saved in **one SQL transaction**. Either both succeed or both roll back. The Outbox Processor publishes independently — if Service Bus is temporarily unavailable, the next timer execution retries automatically.

---

### 2. CQRS Pipeline — Cross-Cutting Concerns Applied Once

Every MediatR request (command or query) flows through three pipeline behaviours before reaching the handler:

```
Request
  → LoggingBehaviour       (structured telemetry, timing, slow-request warnings)
    → ValidationBehaviour  (FluentValidation — rejects before handler executes)
      → UnhandledExceptionBehaviour  (domain exceptions as Warning, bugs as Error)
        → Handler
```

No handler contains logging, validation orchestration, or exception catching code. These concerns are defined once and apply everywhere — including Azure Function calls to MediatR.

---

### 3. Two DbContexts — Domain and Identity Separated

`AppDbContext` owns all business tables. `NexaStoreIdentityDbContext` owns all ASP.NET Core Identity tables (`AspNetUsers`, `AspNetRoles` etc.).

**Why:** Swapping the auth provider (e.g. moving to Keycloak or Azure AD B2C) means replacing the Identity layer only — no business schema is touched. Migrations for business and auth evolve independently.

---

### 4. Redis Cache-Aside Pattern

Product catalog queries use the Cache-Aside Pattern:

```
GetProductsQueryHandler
 ├── Build deterministic cache key from all query parameters
 ├── Check Redis → HIT  → return immediately (zero DB calls)
 └──             → MISS → query DB → map to DTO → store in Redis → return
```

Write handlers (`CreateProduct`, `UpdateProduct`, `DeleteProduct`) bust the cache immediately on success. TTL (5 min list, 10 min detail) acts as a safety net if cache invalidation fails.

---

### 5. JWT + Refresh Token Rotation

- **Access token**: 60 minutes, stateless, validated on every request from claims — no DB call
- **Refresh token**: 7 days, stored on `ApplicationUser`, cryptographically random (CSPRNG, not Guid)
- **Rotation**: every refresh issues a new token pair and invalidates the old one — stolen refresh tokens are detected when the legitimate user next refreshes

---

## API Endpoints

### Authentication
```
POST   /api/v1/auth/register       Register new customer account
POST   /api/v1/auth/login          Login and receive JWT + refresh token
POST   /api/v1/auth/refresh-token  Exchange refresh token for new token pair
```

### Products
```
GET    /api/v1/products            Paged + filtered + sorted product list (cached)
GET    /api/v1/products/{id}       Single product detail (cached)
POST   /api/v1/products            Create product              [Admin]
PUT    /api/v1/products/{id}       Update product              [Admin]
DELETE /api/v1/products/{id}       Delete product              [Admin]
```

### Orders
```
GET    /api/v1/orders              Paged orders (own for Customer, all for Admin)
GET    /api/v1/orders/{id}         Order detail with line items
POST   /api/v1/orders              Place new order             [Customer]
PUT    /api/v1/orders/{id}/cancel  Cancel order                [Customer / Admin]
PUT    /api/v1/orders/{id}/status  Update order status         [Admin]
POST   /api/v1/orders/{id}/payment Process payment             [Customer]
```

### System
```
GET    /health                     Health check — SQL Server + Redis + Service Bus
GET    /swagger                    OpenAPI documentation (Development only)
```

---

## Azure Functions

| Function | Trigger | Job |
|---|---|---|
| `OutboxProcessorFunction` | TimerTrigger — every 10s | Reads unprocessed OutboxMessages → publishes to Azure Service Bus → marks processed |
| `OrderExpiryFunction` | TimerTrigger — every 1hr | Finds Pending orders older than 24hrs → cancels automatically |
| `OrderPlacedConsumerFunction` | ServiceBusTrigger — `order-placed` topic | Receives event → sends order confirmation email |

Functions reuse the same `AddApplicationServices()`, `AddPersistenceServices()`, and `AddInfrastructureServices()` registrations as the API — zero duplicated business logic.

---

## Event Flow — End to End

```
Customer → POST /api/v1/orders
               ↓
    PlaceOrderCommandHandler
      ✓ Validate stock availability
      ✓ Decrement StockQuantity per product
      ✓ Create Order + OrderItems
      ✓ Serialize OrderPlacedEvent → OutboxMessage
      ✓ SaveChangesAsync()  ← atomic transaction
               ↓
    OutboxProcessorFunction  (10s timer)
      ✓ Publish to Azure Service Bus "order-placed" topic
      ✓ Mark OutboxMessage.ProcessedAt
               ↓
    OrderPlacedConsumerFunction  (ServiceBusTrigger)
      ✓ Deserialize OrderPlacedMessage
      ✓ Send confirmation email via IEmailService
               ↓
    Customer inbox ✉️
```

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server) (local or Docker)
- [Redis](https://redis.io/download) (local or Docker)
- [Azure Storage Emulator](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (for Functions local dev)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)

### Quick Start with Docker

```bash
# Start SQL Server + Redis with Docker
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Your_password123" \
  -p 1433:1433 --name nexastore-sql -d mcr.microsoft.com/mssql/server:2022-latest

docker run -p 6379:6379 --name nexastore-redis -d redis:alpine
```

### Clone and Configure

```bash
git clone https://github.com/RaviDeveloper7/NexaStore.git
cd NexaStore
```

Set your secrets via .NET User Secrets (never commit secrets to source control):

```bash
cd src/NexaStore.Api

dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost;Database=NexaStoreDb;User Id=sa;Password=Your_password123;TrustServerCertificate=True;"

dotnet user-secrets set "JwtSettings:Key" "your-super-secret-key-minimum-32-characters"

dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
```

### Apply Migrations

```bash
# Business schema
dotnet ef database update \
  --project src/NexaStore.Persistence \
  --startup-project src/NexaStore.Api \
  --context AppDbContext

# Identity schema
dotnet ef database update \
  --project src/NexaStore.Identity \
  --startup-project src/NexaStore.Api \
  --context NexaStoreIdentityDbContext
```

This applies all migrations and seeds:
- 5 product categories
- 13 sample products across all categories
- Admin and Customer roles

### Run the API

```bash
dotnet run --project src/NexaStore.Api
```

Swagger UI: `https://localhost:7001/swagger`
Health check: `https://localhost:7001/health`

### Run the Functions

```bash
cd src/NexaStore.Functions
func start
```

---

## Configuration Reference

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=NexaStoreDb;..."
  },
  "JwtSettings": {
    "Key": "minimum-32-character-secret",
    "Issuer": "NexaStore",
    "Audience": "NexaStore",
    "DurationInMinutes": 60,
    "RefreshTokenDurationInDays": 7
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "ServiceBus": {
    "ConnectionString": "",
    "OrderPlacedTopic": "order-placed",
    "OrderCancelledTopic": "order-cancelled",
    "PaymentCompletedTopic": "payment-completed"
  },
  "EmailSettings": {
    "ApiKey": "",
    "FromEmail": "noreply@nexastore.com",
    "FromName": "NexaStore"
  },
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
```

> All sensitive values should be stored in Azure Key Vault (production) or .NET User Secrets (development). Never commit secrets to source control.

---

## Running Tests

```bash
# Unit tests — no infrastructure required
dotnet test tests/NexaStore.Application.UnitTests

# Integration tests — requires Docker (Testcontainers spins up SQL Server automatically)
dotnet test tests/NexaStore.Persistence.IntegrationTests

# All tests
dotnet test NexaStore.sln
```

**Unit tests** use Moq to mock all interfaces (`IProductRepository`, `IOrderRepository`, `IUnitOfWork`, `ICacheService`). Handlers are tested in complete isolation — no database, no Redis, no network.

**Integration tests** use Testcontainers to spin up a real SQL Server Docker container per test run. Migrations are applied automatically. Tests run against real EF Core queries — catches issues that mocks can never catch.

---

## CI/CD Pipeline

```
Push to main
     ↓
┌─── deploy-api.yml ─────────────────────────┐
│  1. Setup .NET 8                           │
│  2. Restore NuGet packages                 │
│  3. Build solution                         │
│  4. Run Application.UnitTests              │
│  5. Run Persistence.IntegrationTests       │
│  6. Publish NexaStore.Api                  │
│  7. Deploy → Azure App Service             │
└────────────────────────────────────────────┘
     ↓
┌─── deploy-functions.yml ───────────────────┐
│  1. Setup .NET 8                           │
│  2. Build NexaStore.Functions              │
│  3. Deploy → Azure Function App            │
└────────────────────────────────────────────┘
```

Tests must pass before any deployment. A failing unit test blocks the entire pipeline.

---

## Domain Model

```
Category 1 ──────────── * Product
                              │
                              │ (price snapshot at order time)
                              │
Order 1 ──────────────── * OrderItem * ──── 1 Product
  │
  │ 1
  │
Payment
```

**Order** is the primary aggregate root. It owns `OrderItem` (cascade delete) and raises domain events (`OrderPlacedEvent`, `OrderCancelledEvent`). Payment references Order but is independent — supporting multiple payment attempts per order is a one-line schema change.

**OutboxMessage** stores serialised domain events in the same DB transaction as the Order. It is not a domain entity — it is an infrastructure reliability mechanism that happens to live in the same database.

---

## Project Conventions

### Naming
- Commands return `Guid` (Create) or `Unit` (Update, Delete)
- Queries return DTOs — never domain entities
- Handlers are named `{Feature}CommandHandler` / `{Feature}QueryHandler`
- Validators are named `{Command}Validator`

### Patterns Applied
- **Repository Pattern** — Application never references `DbContext` or EF directly
- **Unit of Work** — all saves go through `IUnitOfWork.SaveChangesAsync()`
- **Options Pattern** — all configuration bound to typed classes (`JwtSettings`, `CacheSettings`, `ServiceBusSettings`)
- **Cache-Aside** — check cache → DB on miss → populate cache
- **Outbox Pattern** — domain events written to DB, published asynchronously
- **Conventional Commits** — `feat` / `fix` / `refactor` / `test` / `chore` / `docs`

### What Belongs Where

| Concern | Layer | Example |
|---|---|---|
| Business rules | Domain | `InsufficientStockException` |
| Input validation | Application | `CreateProductCommandValidator` |
| Orchestration | Application | `PlaceOrderCommandHandler` |
| SQL queries | Persistence | `ProductRepository.GetPagedAsync` |
| External calls | Infrastructure | `AzureServiceBusPublisher` |
| Auth + JWT | Identity | `AuthService.GenerateJwtTokenAsync` |
| HTTP routing | Api | `ProductsController` |
| Background jobs | Functions | `OutboxProcessorFunction` |

---

## Seed Data

Applied automatically when running migrations:

| Category | Products |
|---|---|
| Electronics | Samsung Galaxy S24 Ultra, Apple MacBook Pro M3, Sony WH-1000XM5 |
| Clothing | Nike Air Max 270, Levi's 501 Jeans, The North Face Thermoball Jacket |
| Books | Clean Architecture, Designing Data-Intensive Applications, The Pragmatic Programmer |
| Home & Garden | Instant Pot Duo, Dyson V15 Detect |
| Sports & Outdoors | Garmin Forerunner 265, Bowflex SelectTech 552 |

---

## Commit Strategy

This project follows [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(domain): add Order aggregate root with domain event support
feat(application): add PlaceOrderCommandHandler with atomic outbox save
feat(persistence): add OutboxRepository with filtered index on ProcessedAt
fix(identity): remove HasDefaultValue from OrderStatus to eliminate EF sentinel warning
chore(functions): scaffold Azure Functions v4 isolated worker project
test(application): add PlaceOrderCommandHandlerTests with insufficient stock scenario
```

6–12 commits per working day. One logical unit per commit. Never batch multiple features.

---

## What This Demonstrates

This project was built to be discussed in senior .NET engineering interviews. Every decision has a reason:

- **Why two DbContexts?** Domain and Identity schema evolution independently
- **Why Outbox Pattern?** Dual-write problem — DB write and Service Bus publish must be atomic
- **Why Mapster over AutoMapper?** IL-compiled mapping — benchmarked faster, more explicit config
- **Why filtered index on OutboxMessages?** `WHERE ProcessedAt IS NULL` every 10 seconds — full table scan without it
- **Why `AsNoTracking` on query handlers?** Read-only operations skip EF change tracking overhead
- **Why `CheckPasswordSignInAsync` over `PasswordSignInAsync`?** No session cookie for a JWT API
- **Why enums start at 1?** `0` is CLR default — unset enum would silently map to a valid state
- **Why `ExecuteUpdateAsync` in `MarkAsProcessedAsync`?** Single `UPDATE` vs fetch-then-save (two DB round-trips)
- **Why `ClockSkew = TimeSpan.Zero`?** 5-minute default tolerance is for browser apps — exact expiry for APIs

---

## License

[MIT](LICENSE) — free to use, learn from, and adapt.

---

<div align="center">

Built with intention. Every line has a reason.

</div>

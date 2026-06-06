# Tutora Platform Backend

Backend API for the Tutora tutoring platform.

## Project Layout

- `MV.DomainLayer`: entities, DTOs, constants, helpers, enums
- `MV.ApplicationLayer`: service interfaces and business logic
- `MV.InfrastructureLayer`: repositories, EF Core, external clients
- `MV.PresentationLayer`: ASP.NET Core controllers, middleware, app startup

## Local Development

Use the .NET solution at `Tutora-platform-backend.sln`.

```powershell
dotnet build Tutora-platform-backend.sln
dotnet run --project MV.PresentationLayer
```

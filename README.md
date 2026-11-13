# Vehicle Service API

A portfolio project for registering customers and vehicles and tracking workshop service requests. It uses ASP.NET Core Minimal APIs, Entity Framework Core, and SQL Server.

## Run locally

Install the .NET 10 SDK and start a local SQL Server instance. In PowerShell, set the connection string for your instance. The example uses Windows authentication and a local SQL Server default instance.

```powershell
$env:ConnectionStrings__ServiceDb = 'Server=localhost;Database=VehicleServiceApi;Trusted_Connection=True;TrustServerCertificate=True'
dotnet run -- --init-db
dotnet run -- --urls http://localhost:5080
```

The initialization command creates the schema in a fresh demo database. The SQL Server login needs permission to create the database, or you can provide an empty database with schema creation permissions. This project uses `EnsureCreated` for initial setup. It is a local demo and has no authentication.

## Try the workflow

Run these requests in another PowerShell window while the API is running.

```powershell
$base = 'http://localhost:5080'
$customer = Invoke-RestMethod "$base/customers" -Method Post -ContentType 'application/json' -Body '{"name":"Sample Customer"}'
$vehicle = Invoke-RestMethod "$base/vehicles" -Method Post -ContentType 'application/json' -Body (@{customerId=$customer.id;plate='34 ABC 123'} | ConvertTo-Json)
$request = Invoke-RestMethod "$base/service-requests" -Method Post -ContentType 'application/json' -Body (@{vehicleId=$vehicle.id;description='Oil and filter change'} | ConvertTo-Json)
Invoke-RestMethod "$base/service-requests/$($request.id)/status" -Method Patch -ContentType 'application/json' -Body '{"status":"InProgress"}'
Invoke-RestMethod "$base/service-requests/$($request.id)/status" -Method Patch -ContentType 'application/json' -Body '{"status":"Completed"}'
Invoke-RestMethod "$base/service-requests/$($request.id)"
```

## API routes

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/customers` | Register a customer |
| GET | `/customers/{id}` | Read a customer |
| POST | `/vehicles` | Register a vehicle for a customer |
| GET | `/vehicles/{id}` | Read a vehicle |
| POST | `/service-requests` | Open a service request |
| GET | `/service-requests/{id}` | Read a service request |
| PATCH | `/service-requests/{id}/status` | Advance its status |

A service request starts as `Open`, advances to `InProgress`, and ends at `Completed`. The API returns `400` for invalid input, `404` for missing records, and `409` for duplicate plates or conflicting status updates.

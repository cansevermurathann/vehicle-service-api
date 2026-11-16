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

## OpenAPI contract

Run the API in the Development environment to generate its OpenAPI contract.
The document is available before making database requests.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run -- --urls http://localhost:5080
```

Open `http://localhost:5080/openapi/v1.json` in a browser or import it into
an API client. The document describes the request models and the expected
success and error status codes. The contract endpoint is enabled only in
Development.

## Browse service requests

Use `GET /service-requests` to browse requests in ascending ID order. Supply an
optional `vehicleId` or `status` (`Open`, `InProgress`, or `Completed`) to
narrow the results. Each response contains `items` and `nextAfterId`.

```powershell
$page = Invoke-RestMethod "$base/service-requests?status=Open&limit=10"
if ($null -ne $page.nextAfterId) {
    Invoke-RestMethod "$base/service-requests?status=Open&limit=10&afterId=$($page.nextAfterId)"
}
```

Repeat the same filters when following the cursor. The default page size is 20
and the maximum is 100. A null cursor means there are no more matching rows at
the time of the query. Separate requests do not share a database snapshot.

## Run the HTTP contract tests

From the repository root, with the .NET 10 SDK and Python 3 installed:

```sh
dotnet build
python -m unittest discover -s tests -v
```

The suite launches the Debug build on an ephemeral localhost port. It checks
the service request listing's OpenAPI contract and verifies that invalid
filters and pagination values produce field-specific HTTP 400 responses.
These cases run without SQL Server. Successful database-backed requests
require a separate integration environment.

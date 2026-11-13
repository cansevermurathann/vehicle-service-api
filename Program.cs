using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VehicleServiceApi;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("ServiceDb")
    ?? throw new InvalidOperationException("Set ConnectionStrings__ServiceDb before starting the API.");

builder.Services.AddDbContext<ServiceDb>(options => options.UseSqlServer(connection));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();

if (args.Contains("--init-db"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ServiceDb>().Database.EnsureCreatedAsync();
    return;
}

app.MapGet("/customers/{id:int}", async (int id, ServiceDb db, CancellationToken ct) =>
    await db.Customers.FindAsync([id], ct) is { } customer ? Results.Ok(customer) : Results.NotFound());

app.MapPost("/customers", async (CreateCustomer input, ServiceDb db, CancellationToken ct) =>
{
    var name = input.Name?.Trim();
    if (string.IsNullOrEmpty(name) || name.Length > 120)
        return Invalid("name", "Enter a name of 1 to 120 characters.");

    var customer = new Customer { Name = name };
    db.Customers.Add(customer);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/customers/{customer.Id}", customer);
});

app.MapGet("/vehicles/{id:int}", async (int id, ServiceDb db, CancellationToken ct) =>
    await db.Vehicles.FindAsync([id], ct) is { } vehicle ? Results.Ok(vehicle) : Results.NotFound());

app.MapPost("/vehicles", async (CreateVehicle input, ServiceDb db, CancellationToken ct) =>
{
    var plate = input.Plate?.Trim().Replace(" ", "").ToUpperInvariant();
    if (string.IsNullOrEmpty(plate) || plate.Length > 16 || !plate.All(char.IsAsciiLetterOrDigit))
        return Invalid("plate", "Enter a plate of 1 to 16 letters or digits.");

    if (!await db.Customers.AnyAsync(customer => customer.Id == input.CustomerId, ct))
        return Results.NotFound(new { message = "Customer not found." });

    var vehicle = new Vehicle { CustomerId = input.CustomerId, Plate = plate };
    db.Vehicles.Add(vehicle);
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
    {
        return Results.Conflict(new { message = "This plate is already registered." });
    }

    return Results.Created($"/vehicles/{vehicle.Id}", vehicle);
});

app.MapGet("/service-requests/{id:int}", async (int id, ServiceDb db, CancellationToken ct) =>
    await db.ServiceRequests.FindAsync([id], ct) is { } request ? Results.Ok(request) : Results.NotFound());

app.MapPost("/service-requests", async (OpenServiceRequest input, ServiceDb db, CancellationToken ct) =>
{
    var description = input.Description?.Trim();
    if (string.IsNullOrEmpty(description) || description.Length > 500)
        return Invalid("description", "Enter a description of 1 to 500 characters.");

    if (!await db.Vehicles.AnyAsync(vehicle => vehicle.Id == input.VehicleId, ct))
        return Results.NotFound(new { message = "Vehicle not found." });

    var request = new ServiceRequest { VehicleId = input.VehicleId, Description = description };
    db.ServiceRequests.Add(request);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/service-requests/{request.Id}", request);
});

app.MapPatch("/service-requests/{id:int}/status", async (
    int id, ChangeStatus input, ServiceDb db, CancellationToken ct) =>
{
    if (input.Status is not { } status)
        return Invalid("status", "Provide Open, InProgress, or Completed.");

    var request = await db.ServiceRequests.FindAsync([id], ct);
    if (request is null)
        return Results.NotFound();

    if (request.Status == status)
        return Results.Ok(request);

    var validTransition = (request.Status, status) is
        (ServiceStatus.Open, ServiceStatus.InProgress) or
        (ServiceStatus.InProgress, ServiceStatus.Completed);

    if (!validTransition)
        return Results.Conflict(new { message = "Status must advance from Open to InProgress to Completed." });

    request.Status = status;
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { message = "Status changed concurrently. Reload the request before retrying." });
    }

    return Results.Ok(request);
});

await app.RunAsync();

static IResult Invalid(string field, string message) =>
    Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

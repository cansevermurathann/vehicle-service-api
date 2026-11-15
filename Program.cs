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
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

if (args.Contains("--init-db"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ServiceDb>().Database.EnsureCreatedAsync();
    return;
}

app.MapGet("/customers/{id:int}", async (int id, ServiceDb db, CancellationToken ct) =>
    await db.Customers.FindAsync([id], ct) is { } customer ? Results.Ok(customer) : Results.NotFound())
    .Produces<Customer>()
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/customers", async (CreateCustomer input, ServiceDb db, CancellationToken ct) =>
{
    var name = input.Name?.Trim();
    if (string.IsNullOrEmpty(name) || name.Length > 120)
        return Invalid("name", "Enter a name of 1 to 120 characters.");

    var customer = new Customer { Name = name };
    db.Customers.Add(customer);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/customers/{customer.Id}", customer);
})
    .Produces<Customer>(StatusCodes.Status201Created)
    .ProducesValidationProblem();

app.MapGet("/vehicles/{id:int}", async (int id, ServiceDb db, CancellationToken ct) =>
    await db.Vehicles.FindAsync([id], ct) is { } vehicle ? Results.Ok(vehicle) : Results.NotFound())
    .Produces<Vehicle>()
    .Produces(StatusCodes.Status404NotFound);

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
})
    .Produces<Vehicle>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapGet("/service-requests", async (
    ServiceDb db, CancellationToken ct, int? vehicleId = null, string? status = null,
    int afterId = 0, int limit = 20) =>
{
    if (vehicleId is <= 0)
        return Invalid("vehicleId", "Provide a positive vehicle ID.");
    if (afterId < 0)
        return Invalid("afterId", "Provide a non-negative cursor.");
    if (limit is < 1 or > 100)
        return Invalid("limit", "Choose a page size from 1 to 100.");

    var query = db.ServiceRequests.AsNoTracking().Where(request => request.Id > afterId);
    if (vehicleId is int id)
        query = query.Where(request => request.VehicleId == id);
    if (status is not null)
    {
        if (!Enum.GetNames<ServiceStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
            return Invalid("status", "Provide Open, InProgress, or Completed.");
        var filter = Enum.Parse<ServiceStatus>(status, ignoreCase: true);
        query = query.Where(request => request.Status == filter);
    }

    var items = await query.OrderBy(request => request.Id).Take(limit + 1).ToListAsync(ct);
    int? nextAfterId = null;
    if (items.Count > limit)
    {
        items.RemoveAt(limit);
        nextAfterId = items[^1].Id;
    }

    return Results.Ok(new ServiceRequestPage(items, nextAfterId));
})
    .Produces<ServiceRequestPage>()
    .ProducesValidationProblem();

app.MapGet("/service-requests/{id:int}", async (int id, ServiceDb db, CancellationToken ct) =>
    await db.ServiceRequests.FindAsync([id], ct) is { } request ? Results.Ok(request) : Results.NotFound())
    .Produces<ServiceRequest>()
    .Produces(StatusCodes.Status404NotFound);

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
})
    .Produces<ServiceRequest>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status404NotFound);

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
})
    .Produces<ServiceRequest>()
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

await app.RunAsync();

static IResult Invalid(string field, string message) =>
    Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

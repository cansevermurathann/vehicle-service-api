using Microsoft.EntityFrameworkCore;

namespace VehicleServiceApi;

public sealed class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Vehicle
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public required string Plate { get; set; }
}

public sealed class ServiceRequest
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public required string Description { get; set; }
    public ServiceStatus Status { get; set; } = ServiceStatus.Open;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ServiceStatus
{
    Open,
    InProgress,
    Completed
}

public sealed class ServiceDb(DbContextOptions<ServiceDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Customer>().Property(customer => customer.Name).HasMaxLength(120);
        model.Entity<Vehicle>().Property(vehicle => vehicle.Plate).HasMaxLength(16);
        model.Entity<Vehicle>().HasIndex(vehicle => vehicle.Plate).IsUnique();
        model.Entity<Vehicle>().HasOne<Customer>().WithMany().HasForeignKey(vehicle => vehicle.CustomerId);
        model.Entity<ServiceRequest>().Property(request => request.Description).HasMaxLength(500);
        model.Entity<ServiceRequest>().Property(request => request.Status).IsConcurrencyToken();
        model.Entity<ServiceRequest>().HasOne<Vehicle>().WithMany().HasForeignKey(request => request.VehicleId);
    }
}

public sealed record CreateCustomer(string? Name);
public sealed record CreateVehicle(int CustomerId, string? Plate);
public sealed record OpenServiceRequest(int VehicleId, string? Description);
public sealed record ChangeStatus(ServiceStatus? Status);

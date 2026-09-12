using LifeLink.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Apply migrations automatically if any pending
            await context.Database.MigrateAsync();

            // 1. Seed Roles
            if (!await context.Roles.AnyAsync())
            {
                var roles = new List<Role>
                {
                    new() { RoleName = "Admin" },
                    new() { RoleName = "Donor" },
                    new() { RoleName = "Receiver" },
                    new() { RoleName = "Hospital" }
                };
                await context.Roles.AddRangeAsync(roles);
                await context.SaveChangesAsync();
            }

            var adminRole = await context.Roles.FirstAsync(r => r.RoleName == "Admin");
            var donorRole = await context.Roles.FirstAsync(r => r.RoleName == "Donor");
            var receiverRole = await context.Roles.FirstAsync(r => r.RoleName == "Receiver");
            var hospitalRole = await context.Roles.FirstAsync(r => r.RoleName == "Hospital");

            // 2. Seed Admin User
            if (!await context.Users.AnyAsync(u => u.Email == "admin@lifelink.com"))
            {
                var adminUser = new User
                {
                    Name = "System Administrator",
                    Email = "admin@lifelink.com",
                    PasswordHash = "admin@123", // In production, use hashed passwords
                    Phone = "+880-1700-000000",
                    RoleId = adminRole.RoleId,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };
                await context.Users.AddAsync(adminUser);
                await context.SaveChangesAsync();
            }

            // 2.1 Seed Hospital Representative User
            if (!await context.Users.AnyAsync(u => u.Email == "dmc@hospital.com"))
            {
                var hospitalUser = new User
                {
                    Name = "Dhaka Medical College Hospital",
                    Email = "dmc@hospital.com",
                    PasswordHash = "hospital@123",
                    Phone = "+880-2-55165088",
                    RoleId = hospitalRole.RoleId,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };
                await context.Users.AddAsync(hospitalUser);
                await context.SaveChangesAsync();
            }

            // 3. Seed Hospitals
            if (!await context.Hospitals.AnyAsync())
            {
                var hospitals = new List<Hospital>
                {
                    new() { Name = "Dhaka Medical College Hospital", Address = "Secretariat Rd, Dhaka 1000", Location = "Dhaka", Contact = "+880-2-55165088", VerificationStatus = "Verified" },
                    new() { Name = "Chittagong Medical College Hospital", Address = "57 K.B. Fazlul Kader Rd, Chattogram", Location = "Chittagong", Contact = "+880-31-619400", VerificationStatus = "Verified" },
                    new() { Name = "Square Hospital", Address = "18/F Bir Uttam Qazi Nuruzzaman Sarak, Dhaka 1205", Location = "Dhaka", Contact = "+880-2-8159457", VerificationStatus = "Verified" },
                    new() { Name = "Sylhet MAG Osmani Medical College", Address = "Kajalshah, Sylhet 3100", Location = "Sylhet", Contact = "+880-821-713487", VerificationStatus = "Verified" },
                    new() { Name = "Rajshahi Medical College Hospital", Address = "Laxmipur, Rajshahi 6000", Location = "Rajshahi", Contact = "+880-721-772150", VerificationStatus = "Verified" }
                };
                await context.Hospitals.AddRangeAsync(hospitals);
                await context.SaveChangesAsync();
            }

            var dmcHospital = await context.Hospitals.FirstAsync();

            // 4. Seed Blood Stock
            if (!await context.BloodStock.AnyAsync())
            {
                var bloodGroups = new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };
                var random = new Random(42);
                var stocks = new List<BloodStock>();

                foreach (var hospital in await context.Hospitals.ToListAsync())
                {
                    foreach (var bg in bloodGroups)
                    {
                        int qty = random.Next(5, 35);
                        stocks.Add(new BloodStock
                        {
                            HospitalId = hospital.HospitalId,
                            BloodGroup = bg,
                            Quantity = qty,
                            ExpiryDate = DateTime.UtcNow.AddDays(random.Next(10, 42)),
                            Status = qty < 10 ? "Low Stock" : "Available",
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }
                await context.BloodStock.AddRangeAsync(stocks);
                await context.SaveChangesAsync();
            }

            // 5. Seed Donors (User + DonorProfile)
            if (!await context.DonorProfiles.AnyAsync())
            {
                var sampleDonors = new[]
                {
                    new { Name = "Ahmed Khan", Email = "ahmed@email.com", Phone = "+880-1234-567890", Blood = "O+", Loc = "Dhaka", Age = 28, LastDon = DateTime.UtcNow.AddMonths(-4) },
                    new { Name = "Fatima Ahmed", Email = "fatima@email.com", Phone = "+880-9876-543210", Blood = "A+", Loc = "Chittagong", Age = 25, LastDon = DateTime.UtcNow.AddMonths(-2) },
                    new { Name = "Rahul Das", Email = "rahul@email.com", Phone = "+880-5555-123456", Blood = "B+", Loc = "Sylhet", Age = 32, LastDon = DateTime.UtcNow.AddMonths(-6) },
                    new { Name = "Sara Ali", Email = "sara@email.com", Phone = "+880-7777-888899", Blood = "AB+", Loc = "Rajshahi", Age = 23, LastDon = DateTime.UtcNow.AddMonths(-1) },
                    new { Name = "Kamal Hossain", Email = "kamal@email.com", Phone = "+880-3333-444455", Blood = "O-", Loc = "Khulna", Age = 35, LastDon = DateTime.UtcNow.AddMonths(-5) },
                    new { Name = "Nusrat Jahan", Email = "nusrat@email.com", Phone = "+880-1811-223344", Blood = "A-", Loc = "Dhaka", Age = 27, LastDon = DateTime.UtcNow.AddMonths(-3) },
                    new { Name = "Tanvir Rahman", Email = "tanvir@email.com", Phone = "+880-1922-334455", Blood = "B-", Loc = "Chittagong", Age = 29, LastDon = DateTime.UtcNow.AddMonths(-7) },
                    new { Name = "Mehedi Hasan", Email = "mehedi@email.com", Phone = "+880-1633-445566", Blood = "AB-", Loc = "Dhaka", Age = 31, LastDon = DateTime.UtcNow.AddMonths(-4) }
                };

                foreach (var d in sampleDonors)
                {
                    var u = new User
                    {
                        Name = d.Name,
                        Email = d.Email,
                        PasswordHash = "donor@123",
                        Phone = d.Phone,
                        RoleId = donorRole.RoleId,
                        Status = "Active",
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Users.AddAsync(u);
                    await context.SaveChangesAsync();

                    var dp = new DonorProfile
                    {
                        UserId = u.UserId,
                        BloodGroup = d.Blood,
                        DateOfBirth = DateTime.UtcNow.AddYears(-d.Age),
                        Location = d.Loc,
                        Availability = true,
                        LastDonationDate = d.LastDon,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.DonorProfiles.AddAsync(dp);
                    await context.SaveChangesAsync();

                    // Add a donation history record
                    await context.DonationHistory.AddAsync(new DonationHistory
                    {
                        DonorId = dp.DonorId,
                        HospitalId = dmcHospital.HospitalId,
                        DonationDate = DateTime.UtcNow.AddMonths(-3),
                        Status = "Completed"
                    });
                }
                await context.SaveChangesAsync();
            }

            // 6. Seed Blood Requests (Receiver User + BloodRequest)
            if (!await context.BloodRequests.AnyAsync())
            {
                var sampleRequests = new[]
                {
                    new { Name = "Karim Hossain", Email = "karim@email.com", Phone = "+880-1111-222233", Blood = "O-", Qty = 2, Urgency = "Critical", Loc = "Dhaka", ReqDate = DateTime.UtcNow.AddDays(1) },
                    new { Name = "Sara Begum", Email = "sara.begum@email.com", Phone = "+880-4444-555566", Blood = "A+", Qty = 1, Urgency = "Urgent", Loc = "Chittagong", ReqDate = DateTime.UtcNow.AddDays(2) },
                    new { Name = "Rahim Ali", Email = "rahim@email.com", Phone = "+880-7777-888899", Blood = "B+", Qty = 3, Urgency = "Normal", Loc = "Sylhet", ReqDate = DateTime.UtcNow.AddDays(4) }
                };

                foreach (var r in sampleRequests)
                {
                    var u = new User
                    {
                        Name = r.Name,
                        Email = r.Email,
                        PasswordHash = "receiver@123",
                        Phone = r.Phone,
                        RoleId = receiverRole.RoleId,
                        Status = "Active",
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Users.AddAsync(u);
                    await context.SaveChangesAsync();

                    var br = new BloodRequest
                    {
                        ReceiverId = u.UserId,
                        BloodGroup = r.Blood,
                        Quantity = r.Qty,
                        HospitalId = dmcHospital.HospitalId,
                        Location = r.Loc,
                        EmergencyLevel = r.Urgency,
                        RequestDate = DateTime.UtcNow,
                        RequiredDate = r.ReqDate,
                        Status = "Pending"
                    };
                    await context.BloodRequests.AddAsync(br);
                }
                await context.SaveChangesAsync();
            }
        }
    }
}

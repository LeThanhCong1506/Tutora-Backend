using System;
using System.Linq;
using MV.InfrastructureLayer.DBContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

class Program {
    static void Main() {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseNpgsql("Host=localhost;Database=agora;Username=postgres;Password=password")
            .Options;
        using var db = new AgoraDbContext(options);
        
        Console.WriteLine("\n--- STUDENTS ---");
        var students = db.Studentprofiles.Where(s => s.Fullname.Contains("Khanh")).ToList();
        foreach (var s in students) {
            Console.WriteLine($"StudentId: {s.Studentid}, LinkedUserId: {s.Linkeduserid}, Name: {s.Fullname}");
            
            Console.WriteLine("  -- Bookings for this student --");
            var bookings = db.Bookings.Where(b => b.Studentid == s.Studentid).ToList();
            if (bookings.Count == 0) Console.WriteLine("    No bookings.");
            foreach(var b in bookings) {
                Console.WriteLine($"    BookingId: {b.Bookingid}, Subj: {b.Subjectid}, Status: {b.Status}");
            }
        }
        
        Console.WriteLine("\n--- USERS ---");
        var user = db.Users.FirstOrDefault(u => u.Email == "khanhlqse171151@fpt.edu.vn");
        if (user != null) {
            Console.WriteLine($"User: {user.Userid}, Email: {user.Email}, Name: {user.Fullname}");
        }
    }
}

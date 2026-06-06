using Npgsql;
using System;

class Program
{
    static void Main()
    {
        string connStr = "Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.swssawtycvwldyctolst;Password=8F8bU7E72Y!G4$b;";
        using var conn = new NpgsqlConnection(connStr);
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT bookingId, status, paymentStatus, studentId, parentId, tutorId
            FROM bookings
            ORDER BY bookingId DESC LIMIT 10;
        ", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"ID: {reader.GetInt32(0)}, Status: {reader.GetString(1)}, PmtStatus: {reader.GetString(2)}, Student: {reader.GetString(3)}, Parent: {reader.GetString(4)}, Tutor: {reader.GetString(5)}");
        }
    }
}

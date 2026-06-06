using System;
using Npgsql;
using System.Collections.Generic;

namespace UpdateDb
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string connStr = "Host=localhost;Port=5432;Database=AGORA-DB;Username=postgres;Password=12345;TrustServerCertificate=true";

                using var conn = new NpgsqlConnection(connStr);
                conn.Open();

                // 1. Get the parent ID
                string parentId = null;
                using var cmdParent = new NpgsqlCommand("SELECT userid FROM users WHERE email = 'unclek293@gmail.com'", conn);
                using var readerParent = cmdParent.ExecuteReader();
                if (readerParent.Read())
                {
                    parentId = readerParent.GetString(0);
                    Console.WriteLine("Parent ID found: " + parentId);
                }
                readerParent.Close();

                if (string.IsNullOrEmpty(parentId))
                {
                    Console.WriteLine("Parent not found.");
                    return;
                }

                // 2. Get up to 3 bookings for this parent
                List<int> bookingIds = new List<int>();
                using var cmdBookings = new NpgsqlCommand("SELECT bookingid FROM bookings WHERE parentid = @pid ORDER BY bookingid DESC LIMIT 3", conn);
                cmdBookings.Parameters.AddWithValue("pid", parentId);
                using var readerBookings = cmdBookings.ExecuteReader();
                while (readerBookings.Read())
                {
                    bookingIds.Add(readerBookings.GetInt32(0));
                }
                readerBookings.Close();

                if (bookingIds.Count < 3)
                {
                    Console.WriteLine($"Not enough bookings found. Found {bookingIds.Count}");
                }

                if (bookingIds.Count > 0)
                {
                    int b1 = bookingIds.Count > 0 ? bookingIds[0] : 0; // ongoing, Escrowed
                    int b2 = bookingIds.Count > 1 ? bookingIds[1] : 0; // deposit_paid, DepositEscrowed
                    int b3 = bookingIds.Count > 2 ? bookingIds[2] : 0; // pending_tutor, Pending
                    
                    if (b1 > 0)
                    {
                        using var cmdUpdate1 = new NpgsqlCommand("UPDATE bookings SET status = 'ongoing', paymentStatus = 'Escrowed', remainingPaidAt = CURRENT_TIMESTAMP WHERE bookingId = @bid", conn);
                        cmdUpdate1.Parameters.AddWithValue("bid", b1);
                        cmdUpdate1.ExecuteNonQuery();
                        Console.WriteLine($"Set Booking {b1} to ongoing/Escrowed");
                    }
                    if (b2 > 0)
                    {
                        using var cmdUpdate2 = new NpgsqlCommand("UPDATE bookings SET status = 'deposit_paid', paymentStatus = 'DepositEscrowed' WHERE bookingId = @bid", conn);
                        cmdUpdate2.Parameters.AddWithValue("bid", b2);
                        cmdUpdate2.ExecuteNonQuery();
                        Console.WriteLine($"Set Booking {b2} to deposit_paid/DepositEscrowed");
                    }
                    if (b3 > 0)
                    {
                        using var cmdUpdate3 = new NpgsqlCommand("UPDATE bookings SET status = 'pending_tutor', paymentStatus = 'Pending' WHERE bookingId = @bid", conn);
                        cmdUpdate3.Parameters.AddWithValue("bid", b3);
                        cmdUpdate3.ExecuteNonQuery();
                        Console.WriteLine($"Set Booking {b3} to pending_tutor/Pending");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}

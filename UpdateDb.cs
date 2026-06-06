using System;
using Npgsql;

class Program
{
    static void Main()
    {
        string connStr = ""Host=localhost;Port=5432;Database=AGORA-DB;Username=postgres;Password=12345;TrustServerCertificate=true"";
        using var conn = new NpgsqlConnection(connStr);
        try {
            conn.Open();
            Console.WriteLine(""Connected!"");
            
            var sql = @""
                ALTER TABLE chatchannels ADD COLUMN IF NOT EXISTS studentid character varying(50);
                ALTER TABLE chatchannels DROP CONSTRAINT IF EXISTS chatchannels_studentid_fkey;
                ALTER TABLE chatchannels ADD CONSTRAINT chatchannels_studentid_fkey FOREIGN KEY (studentid) REFERENCES users(userid) ON DELETE SET NULL;
                DROP INDEX IF EXISTS ix_chatchannels_studentid_tutorid;
                CREATE UNIQUE INDEX ix_chatchannels_studentid_tutorid ON chatchannels (studentid, tutorid) WHERE studentid IS NOT NULL AND tutorid IS NOT NULL;
                ALTER TABLE bookings ADD COLUMN IF NOT EXISTS createdbyrole character varying(20);
            "";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
            Console.WriteLine(""Done!"");
        } catch (Exception ex) {
            Console.WriteLine(""Error: "" + ex.Message);
        }
    }
}

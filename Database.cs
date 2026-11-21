using System;
using System.Configuration;
using System.Data;
using System.Security.Cryptography;
using Npgsql;

namespace Cyusa_Divince_Lab3
{
    internal static class Database
    {
        private static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["DairyDb"].ConnectionString; } }

        public static NpgsqlConnection Open()
        {
            var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        public static void Initialize()
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
            var database = builder.Database;
            builder.Database = "postgres";
            using (var maintenance = new NpgsqlConnection(builder.ConnectionString))
            {
                maintenance.Open();
                using (var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", maintenance))
                {
                    command.Parameters.AddWithValue("name", database);
                    if (command.ExecuteScalar() == null)
                    {
                        using (var create = new NpgsqlCommand("CREATE DATABASE \"" + database.Replace("\"", "\"\"") + "\"", maintenance))
                            create.ExecuteNonQuery();
                    }
                }
            }

            using (var connection = Open())
            using (var command = new NpgsqlCommand(Schema, connection))
                command.ExecuteNonQuery();
        }

        public static bool HasStaff()
        {
            using (var connection = Open())
            using (var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM staff)", connection))
                return (bool)command.ExecuteScalar();
        }

        public static void CreateStaff(string username, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Username and password are required.");
            using (var connection = Open())
            using (var command = new NpgsqlCommand("INSERT INTO staff(username, password_hash, role) VALUES (@u,@p,@r)", connection))
            {
                command.Parameters.AddWithValue("u", username.Trim());
                command.Parameters.AddWithValue("p", HashPassword(password));
                command.Parameters.AddWithValue("r", role);
                command.ExecuteNonQuery();
            }
        }

        public static Staff Login(string username, string password)
        {
            using (var connection = Open())
            using (var command = new NpgsqlCommand("SELECT id, username, password_hash, role FROM staff WHERE lower(username)=lower(@u)", connection))
            {
                command.Parameters.AddWithValue("u", username.Trim());
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || !VerifyPassword(password, reader.GetString(2))) return null;
                    return new Staff { Id = reader.GetInt32(0), Username = reader.GetString(1), Role = reader.GetString(3) };
                }
            }
        }

        public static DataTable Query(string sql, params NpgsqlParameter[] parameters)
        {
            using (var connection = Open())
            using (var command = new NpgsqlCommand(sql, connection))
            using (var adapter = new NpgsqlDataAdapter(command))
            {
                if (parameters != null) command.Parameters.AddRange(parameters);
                var table = new DataTable();
                adapter.Fill(table);
                return table;
            }
        }

        public static int Execute(string sql, params NpgsqlParameter[] parameters)
        {
            using (var connection = Open())
            using (var command = new NpgsqlCommand(sql, connection))
            {
                if (parameters != null) command.Parameters.AddRange(parameters);
                return command.ExecuteNonQuery();
            }
        }

        public static NpgsqlParameter Parameter(string name, object value)
        {
            return new NpgsqlParameter(name, value ?? DBNull.Value);
        }

        private static string HashPassword(string password)
        {
            var salt = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            using (var derive = new Rfc2898DeriveBytes(password, salt, 100000))
                return "100000:" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(derive.GetBytes(32));
        }

        private static bool VerifyPassword(string password, string stored)
        {
            var parts = stored.Split(':');
            if (parts.Length != 3) return false;
            using (var derive = new Rfc2898DeriveBytes(password, Convert.FromBase64String(parts[1]), int.Parse(parts[0])))
            {
                var actual = derive.GetBytes(32);
                var expected = Convert.FromBase64String(parts[2]);
                if (actual.Length != expected.Length) return false;
                var result = 0;
                for (var i = 0; i < actual.Length; i++) result |= actual[i] ^ expected[i];
                return result == 0;
            }
        }

        private const string Schema = @"
CREATE TABLE IF NOT EXISTS staff (
    id SERIAL PRIMARY KEY, username VARCHAR(80) NOT NULL UNIQUE,
    password_hash TEXT NOT NULL, role VARCHAR(20) NOT NULL CHECK (role IN ('Administrator','Clerk','Treasurer')),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE SEQUENCE IF NOT EXISTS farmer_member_number_seq;
CREATE TABLE IF NOT EXISTS farmers (
    id SERIAL PRIMARY KEY, member_number VARCHAR(40) NOT NULL UNIQUE DEFAULT ('M-' || nextval('farmer_member_number_seq')::text),
    name VARCHAR(120) NOT NULL, phone VARCHAR(40) NOT NULL, address VARCHAR(120) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'farmers' AND column_name = 'village') THEN
        ALTER TABLE farmers RENAME COLUMN village TO address;
    END IF;
END $$;
ALTER TABLE farmers ALTER COLUMN member_number SET DEFAULT ('M-' || nextval('farmer_member_number_seq')::text);
CREATE TABLE IF NOT EXISTS deliveries (
    id SERIAL PRIMARY KEY, farmer_id INTEGER NOT NULL REFERENCES farmers(id),
    delivery_date DATE NOT NULL, session VARCHAR(10) NOT NULL CHECK (session IN ('Morning','Evening')),
    litres NUMERIC(10,2) NOT NULL CHECK (litres > 0), status VARCHAR(10) NOT NULL CHECK (status IN ('Accepted','Rejected')),
    rejection_reason VARCHAR(250), recorded_by INTEGER REFERENCES staff(id), created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT one_delivery_per_session UNIQUE(farmer_id, delivery_date, session),
    CONSTRAINT rejected_requires_reason CHECK (status = 'Accepted' OR NULLIF(TRIM(rejection_reason), '') IS NOT NULL)
);
CREATE TABLE IF NOT EXISTS payment_periods (
    id SERIAL PRIMARY KEY, name VARCHAR(100) NOT NULL UNIQUE, start_date DATE NOT NULL, end_date DATE NOT NULL,
    price_per_litre NUMERIC(10,2) NOT NULL CHECK (price_per_litre >= 0), active BOOLEAN NOT NULL DEFAULT TRUE,
    CHECK (end_date >= start_date)
);
CREATE TABLE IF NOT EXISTS payments (
    id SERIAL PRIMARY KEY, period_id INTEGER NOT NULL REFERENCES payment_periods(id), farmer_id INTEGER NOT NULL REFERENCES farmers(id),
    accepted_litres NUMERIC(12,2) NOT NULL DEFAULT 0, amount NUMERIC(12,2) NOT NULL DEFAULT 0,
    status VARCHAR(10) NOT NULL DEFAULT 'Unpaid' CHECK (status IN ('Unpaid','Paid')),
    payment_date DATE, payment_note VARCHAR(250), paid_by INTEGER REFERENCES staff(id),
    UNIQUE(period_id, farmer_id),
    CHECK (status = 'Unpaid' OR (payment_date IS NOT NULL AND NULLIF(TRIM(payment_note), '') IS NOT NULL))
);";
    }

    public sealed class Staff
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }
    }
}

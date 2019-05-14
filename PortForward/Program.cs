using System;
using System.Threading.Tasks;
using SshUtils;
using Npgsql;

namespace console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using (var portForward = await PortForward.ForwardAsync("tmds@192.168.100.169:/var/run/postgresql/.s.PGSQL.5432"))
            {
                var connectionString = $"Server={portForward.IPEndPoint.Address};Port={portForward.IPEndPoint.Port};Database=postgres;User ID=tmds";
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine($"PostgreSQL version: {connection.PostgreSqlVersion}");
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Management.Automation;
using System.Security;
using System.Threading;

namespace RemoteAccessStatistics
{
    class Program
    {
        private static Timer _timer;
        private static string _connectionString = "Data Source=sqlServer,Port\\instanceName;Initial Catalog=vpn_clients;User Id=sa;Password=your_password;";
        private static string _username = "Administrator";
        private static string _password = "your_password";
        private static string _logFilePath = "log.txt";

        static void Main(string[] args)
        {
            int interval = 60000; // 60 seconds
            _timer = new Timer(UpdateDatabase, null, 0, interval);

            Log("The application is running. To stop it, press 'q' and Enter.");
            Console.WriteLine("The application is running. To stop it, press 'q' and Enter.");

            while (Console.Read() != 'q') { }
        }

        static void UpdateDatabase(object state)
        {
            var remoteAccessList = GetRemoteAccessData();

            if (remoteAccessList != null)
            {
                UpdateDatabaseTable(remoteAccessList);
                Log("Database successfully updated.");
                Console.WriteLine("Database successfully updated.");

                RemoveDoubles();
            }
            else
            {
                Log("Error: Failed to retrieve data from the PowerShell server.");
                Console.WriteLine("Error: Failed to retrieve data from the PowerShell server.");
            }
        }

        static List<(string, string, bool)> GetRemoteAccessData()
        {
            var results = new List<(string, string, bool)>();

            SecureString securePassword = new SecureString();
            foreach (char c in _password)
            {
                securePassword.AppendChar(c);
            }
            PSCredential credential = new PSCredential(_username, securePassword);

            List<string> servers = new List<string> { "serverIp1", "serverIp2", "serverIp3" };

            using (PowerShell powerShell = PowerShell.Create())
            {
                foreach (var server in servers)
                {
                    try
                    {

                        // You have to adapt this script to your own formatting needs to insert the name of the vpn clients the way you want.

                        powerShell.AddScript($@"
                            $server = ""{server}""
                            $username = ""{_username}""
                            $password = ConvertTo-SecureString ""{_password}"" -AsPlainText -Force
                            $credential = New-Object System.Management.Automation.PSCredential($username, $password)
                            Invoke-Command -ComputerName $server -ScriptBlock {{
                                Get-RemoteAccessConnectionStatistics | ForEach-Object {{
                                    $ip = $_.ClientIPAddress.IPAddressToString;
                                    $username = $_.Username.split('\\')[1];
                                    if ($username -like '*.vpn') {{
                                        $username = $username -replace '\.vpn$'
                                    }} elseif ($username -match '^(.+)\.([^.]+)$') {{
                                        $username = $matches[1] + ' ' + $matches[2]
                                    }}
                                    return $ip, $username
                                }}
                            }} -Credential $credential");

                        var psResults = powerShell.Invoke();

                        if (!powerShell.HadErrors && psResults.Count % 2 == 0)
                        {
                            for (int i = 0; i < psResults.Count; i += 2)
                            {
                                string ipAddress = psResults[i].ToString();
                                string usernamePart = psResults[i + 1].ToString();
                                results.Add((ipAddress, usernamePart, true));
                            }
                        }
                        else if (powerShell.Streams.Error.Count > 0)
                        {
                            foreach (var error in powerShell.Streams.Error)
                            {
                                Log($"Server {server} PowerShell error: {error.ToString()}");
                                Console.WriteLine($"Server {server} PowerShell error: {error.ToString()}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Server error {server}: {ex.Message}\n{ex.StackTrace}");
                        Console.WriteLine($"Server error {server}: {ex.Message}\n{ex.StackTrace}");
                    }
                    finally
                    {
                        powerShell.Commands.Clear();
                    }
                }
            }

            return results;
        }

        static void UpdateDatabaseTable(List<(string, string, bool)> data)
        {
            if (data.Count == 0)
            {
                Console.WriteLine("No data available for update.");
                return;
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                string markAllDisconnectedQuery = @"
                    UPDATE clienti 
                    SET OnlineStatus = 0 
                    WHERE LastSeen < DATEADD(minute, -1, GETDATE())";

                using (SqlCommand markCmd = new SqlCommand(markAllDisconnectedQuery, connection))
                {
                    markCmd.ExecuteNonQuery();
                }

                foreach (var item in data)
                {
                    string updateQuery = @"
                        IF EXISTS (SELECT * FROM clienti WHERE IPAddress = @ip)
                        BEGIN
                            UPDATE clienti 
                            SET OnlineStatus = @status, 
                                Username = @user, 
                                LastSeen = GETDATE()
                            WHERE IPAddress = @ip
                        END
                        ELSE
                        BEGIN
                            INSERT INTO clienti 
                                (IPAddress, Username, OnlineStatus, LastSeen)
                            VALUES 
                                (@ip, @user, @status, GETDATE())
                        END";

                    using (SqlCommand cmd = new SqlCommand(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@ip", item.Item1);
                        cmd.Parameters.AddWithValue("@user", item.Item2);
                        cmd.Parameters.AddWithValue("@status", item.Item3 ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        static void RemoveDoubles()
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                string ipCleanupQuery = @"
                    DELETE c1
                    OUTPUT DELETED.IPAddress, DELETED.Username
                    FROM clienti c1
                    WHERE EXISTS (
                        SELECT 1
                        FROM clienti c2
                        WHERE c1.IPAddress = c2.IPAddress
                        AND c1.Username <> c2.Username
                        AND c2.LastSeen > c1.LastSeen
                    )";

                string userCleanupQuery = @"
                    WITH RankedEntries AS (
                        SELECT *,
                            ROW_NUMBER() OVER (
                                PARTITION BY Username 
                                ORDER BY LastSeen DESC, OnlineStatus DESC
                            ) AS RowNum
                        FROM clienti
                    )
                    DELETE FROM RankedEntries
                    OUTPUT DELETED.IPAddress, DELETED.Username
                    WHERE RowNum > 1";

                using (SqlCommand cmd = new SqlCommand(ipCleanupQuery, connection))
                using (SqlCommand userCmd = new SqlCommand(userCleanupQuery, connection))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string ipAddress = reader.GetString(0);
                            string username = reader.GetString(1);
                            Log($"Deleted IP conflict: {ipAddress}, user: {username}");
                        }
                    }

                    using (SqlDataReader reader = userCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string ipAddress = reader.GetString(0);
                            string username = reader.GetString(1);
                            Log($"Deleted duplicate: {ipAddress}, user: {username}");
                        }
                    }
                }
            }
        }

        static void Log(string message)
        {
            try
            {
                using (StreamWriter writer = File.AppendText(_logFilePath))
                {
                    writer.WriteLine($"{DateTime.Now} - {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logging error: {ex.Message}");
            }
        }
    }
}
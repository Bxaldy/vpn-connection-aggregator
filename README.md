This application goes hand in hand with [VPN-CLIENT-MANAGER](https://github.com/Bxaldy/windows-vpn-client-manager) as it was mainly made for it.


A .NET-based monitoring utility that aggregates VPN client connection statistics from multiple Windows RRAS servers into a centralized SQL database. Designed for network operations teams needing real-time visibility into VPN client statuses.

⚠️ Critical Notice: This implementation contains intentional trade-offs for rapid deployment in trusted environments. Production deployments require security modifications outlined below so for security rewrite the code according to your own infrastructure and security policies.

Key Features
Multi-Server Monitoring: Simultaneously poll multiple RRAS servers

Connection Statistics: Leverages Get-RemoteAccessConnectionStatistics PowerShell cmdlet

Data Normalization:

- Username standardization (VPN suffix handling)

- IP address validation

📚 Database Synchronization:

- Upsert operations for client records

- Online/offline status tracking

- Last-seen timestamp management

💿 Data Integrity: Automated duplicate resolution via:

- IP-based conflict resolution

- Username prioritization

🚨 Operational Resilience:

- Continuous polling (configurable interval)

- Error logging with stack traces 

‼️Known Limitations ‼️

🔐 Authentication is hardcoded
You should replace it with a secure method such as environment variables, encrypted config files, or a secrets manager.

🔌 Uses WinRM for remote execution
You are encouraged to switch to SSH or another secure and modern protocol, especially for production deployments.

📄 Plaintext configuration
All credentials and settings are currently stored in plaintext in the source code. This should never be done in production environments.

🪟 Tightly coupled to RRAS
This utility is specifically built for RRAS-based VPN services on Windows Server. You will need to adapt the logic if using a different VPN solution.

📝📝 SQL DATABASE SCHEME  📝📝
	
 CREATE TABLE clienti (
    IPAddress NVARCHAR(15) PRIMARY KEY,
    Username NVARCHAR(255) NOT NULL,
    OnlineStatus BIT NOT NULL,
    LastSeen DATETIME NOT NULL,
);


‼️‼️Recommendations for Production Use ‼️‼️

- Use environment variables or a secure vault to store all secrets

- Use parameterized config instead of hardcoded values

- Implement structured logging

- Harden the PowerShell logic against malformed data

- Add retry/backoff logic and error resilience

🔍 Recommended Deployment 🔍

While this utility is implemented as a console app, the ideal setup is to run it as a background service.

For quick deployment or testing, it can be launched manually.
However, for long-term use, it is recommended to:

Convert it into a Windows Service
(e.g. using ServiceBase in .NET)

Or use NSSM - the Non-Sucking Service Manager to install the compiled .exe as a Windows service.

This approach ensures:

- Auto-start on system boot

- Restart on crash

- Continuous operation in the background

📝 Author Notes 📝
This application was originally developed to work alongside a WPF-based management console, both created for internal use in a low-budget environment where infrastructure investments were minimal and in-house development was prioritized.

A more secure version of the backend once existed, with improved authentication and encryption, but unfortunately, I no longer have a backup of that version. If you plan to use this project in production, I strongly encourage you to adapt and harden the authentication methods and infrastructure according to your own environment.

The version you see here was entirely built by me to meet operational needs, despite the technical constraints. I'm sharing it publicly in the hope that it can serve as a starting point, a reference, or a utility for others working with Windows Server RRAS for VPNs. 

It worked like this :

RRAS Servers
    └─ Queried via PowerShell (please don't to it through Win-RM, be safe! )

VPN Aggregator (this app)
    └─ Normalizes connection data
    └─ Injects updates into SQL Server

SQL Server
    └─ Acts as the central data source

WPF Front-End (will be included soon)
    └─ Reads data from SQL
    └─ Integrates with LDAP for user management
    └─ Sends commands to clients (where possible)


😁 Contributions 😁
This project was initially built to quickly address an internal need.
You are welcome to fork, improve, adapt, or rewrite parts of it to better fit your own infrastructure and security requirements.

🚨 I'd say it a thousand times, don't use it as it is... if you care about security. 🚨

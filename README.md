# OpenAutomate Bot Agent

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

## Overview

OpenAutomate Bot Agent is the distributed agent component of the OpenAutomate platform that enables automation execution on target machines. It consists of a Windows Service for background processing and a WPF user interface for configuration and monitoring.

The Bot Agent serves as a bridge between the OpenAutomate server and local automation tasks, providing secure access to credentials, managing execution, and reporting status updates to the central server.

## Features

- **Dual-component architecture**: Windows Service for background operations + WPF UI for user interactions
- **Secure asset management**: Access server-stored credentials with machine key authentication
- **Local API server**: Enables both the UI and Python scripts to interact with the service
- **Python SDK**: Simple interface for automation scripts to access platform features
- **Execution management**: Run, monitor, and control automation processes
- **Secure communication**: Encrypted data exchange with the central server
- **Cross-tenant isolation**: Proper data segregation for multi-tenant environments
- **Comprehensive logging**: Detailed operation logs for troubleshooting

## Architecture

The Bot Agent uses a layered architecture:

```
┌─────────────────┐     ┌───────────────┐     ┌──────────────────────┐
│  WPF UI App     │◄───►│  Local API    │◄───►│  Windows Service     │
│  (User Config)  │     │  (localhost)  │     │  (Background Tasks)  │
└─────────────────┘     └───────┬───────┘     └──────────┬───────────┘
                                │                         │
                                ▼                         ▼
                        ┌───────────────┐         ┌───────────────┐
                        │ Python Scripts│         │ OpenAutomate  │
                        │ (Automation)  │         │ Server (API)  │
                        └───────────────┘         └───────────────┘
```

- **Windows Service**: Runs continuously in the background, managing the local API server, credentials, and server communication
- **WPF UI**: Provides a user-friendly interface for configuration and monitoring
- **Local API Server**: HTTP server on localhost:8080 that enables communication between components
- **Python SDK**: Allows automation scripts to interact with the Bot Agent

## Installation

### System Requirements

- Windows 10/11 or Windows Server 2019/2022
- .NET 8.0 Runtime
- Internet connectivity to OpenAutomate server
- Administrator rights for installation

### Installation Steps

1. Download the latest Bot Agent installer from the releases page
2. Run the installer and follow the on-screen instructions
3. After installation, the WPF application will launch automatically
4. Connect to your OpenAutomate server and connect the agent
5. Verify that the Windows Service is running

## Configuration

Initial configuration via the WPF UI includes:

1. **Server Connection**: Enter the OpenAutomate server URL with tenant slug
2. **Authentication**: Provide the Machine Key of created BotAgent of that Tenant


Advanced configuration options include:

- Local API port configuration
- Log level and location settings
- Performance tuning options
- Execution environment settings

## Usage

### WPF UI Application

Launch the application from the Start menu to:

- View current status and connection state
- Monitor running automations
- Configure agent settings
- View logs and troubleshoot issues

### Python Script Integration

Use the included Python SDK to interact with the Bot Agent:

```python
from openautomateagent import Client

# Initialize client
agent = Client()

# Get assets securely
db_password = agent.get_asset("db_password")
api_key = agent.get_asset("api_key")

# Update execution status
agent.update_status("processing_data")

# Log messages
agent.log("Data processing complete", level="info")
```

### Asset Retrieval

Assets (such as credentials, connection strings, and configuration values) are stored securely on the OpenAutomate server and can be accessed by authorized bot agents. The machine key authentication ensures that:

1. Only connected bot agents can request assets
2. Bot agents can only access assets explicitly granted to them
3. All access attempts are logged for audit purposes

## Troubleshooting

Common issues and their solutions:

| Issue | Solution |
|-------|----------|
| Service not starting | Check Windows Services, verify user permissions |
| Connection failure | Ensure server URL is correct and network connectivity exists |
| Registration errors | Verify tenant BotAgent Machine Key and Machine Name correct|
| Python SDK errors | Ensure local API server is running, check port configuration |

Logs are available at:
- Windows Event Log
- `C:\ProgramData\OpenAutomate\BotAgent\logs\`
- Via the WPF UI application (Logs tab)

## Development

For developers extending or customizing the Bot Agent:

- Solution is built with Visual Studio 2022
- Uses .NET 8.0 for both WPF and Service components
- WiX Toolset for installer creation
- Python SDK requires Python 3.8+

### Project Structure

```
OpenAutomate.BotAgent/
├── OpenAutomate.BotAgent.Service/     # Windows Service
├── OpenAutomate.BotAgent.UI/          # WPF Application
├── OpenAutomate.BotAgent.Common/      # Shared code and models
├── OpenAutomate.BotAgent.Installer/   # WiX installer project
└── OpenAutomate.BotAgent.SDK/         # Python SDK
```

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For issues, feature requests, or questions:
- File an issue on GitHub
- Contact your OpenAutomate administrator
- Email support at support@openautomateorg.com
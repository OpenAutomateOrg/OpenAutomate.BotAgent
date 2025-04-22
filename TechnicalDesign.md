# OpenAutomate BotAgent - Technical Design Document

## 1. System Overview

The OpenAutomate BotAgent is a distributed agent solution designed to execute automation tasks on local machines while maintaining secure communication with a central OpenAutomate server. The system consists of multiple components that work together to provide a reliable, secure, and user-friendly automation execution environment.

## 2. High-Level Architecture

The BotAgent follows a multi-layered architecture with the following main components:

```
┌─────────────────────────┐     ┌───────────────────────┐
│  WPF Desktop UI         │◄───►│  OpenAutomate Server  │
│  (Configuration Portal) │     │  (Central Authority)  │
└───────────┬─────────────┘     └───────────┬───────────┘
            │                                │
            ▼                                ▼
┌─────────────────────────┐     ┌───────────────────────┐
│  Windows Service        │◄───►│  Automation Scripts   │
│  (Background Engine)    │     │  (Python w/ SDK)      │
└─────────────────────────┘     └───────────────────────┘
```

### 2.1 System Components

1. **WPF Desktop UI** (.NET 8)
   - User interface for configuration and monitoring
   - Communicates directly with the OpenAutomate server
   - Provides real-time status and connection management
   - Manages secure storage of configuration settings

2. **Windows Service** (.NET 8)
   - Runs in the background as a Windows service
   - Hosts the local API server for script interaction
   - Manages execution of automation tasks
   - Maintains connection with the central server
   - Handles credential and asset management

3. **Local API Server** (ASP.NET Core)
   - Provides HTTP API on localhost for secure local communication
   - Enables Python scripts to interact with the service
   - Exposes endpoints for status, assets, and execution control

4. **Python SDK**
   - Client library for automation scripts
   - Provides simplified interface to the local API
   - Handles authentication and secure communication
   - Manages asset retrieval and status updates

5. **Installer** (WiX Toolset)
   - Windows MSI installer for deployment
   - Handles service installation and registration
   - Configures system permissions and startup behavior

## 3. Detailed Component Design

### 3.1 WPF Desktop UI (OpenAutomate.BotAgent.UI)

#### 3.1.1 MVVM Architecture

The UI follows the MVVM (Model-View-ViewModel) pattern for separation of concerns:

- **Models**: Data structures representing business entities
  - `ConfigurationModel`: Represents agent configuration settings
  - `AgentSettings`: Manages secure storage of settings

- **ViewModels**: Business logic and state management
  - `MainViewModel`: Primary viewmodel handling UI interaction and server communication

- **Views**: User interface elements
  - `MainWindow`: Primary UI window for agent configuration and monitoring

#### 3.1.2 Key Services

- **ApiClient**: Handles HTTP communication with the OpenAutomate server
  - Manages authentication using machine key
  - Handles connection and disconnection requests
  - Processes API responses and error handling

- **AgentSettings**: Secure storage service for configuration
  - Uses Windows DPAPI for encryption
  - Stores connection information and machine key
  - Persists settings between application restarts

#### 3.1.3 UI Flow

1. User launches desktop application
2. Application loads saved settings if available
3. User configures connection parameters (server URL, machine key)
4. User initiates connection to OpenAutomate server
5. Application displays real-time status and connection state
6. User can disconnect or close application when done

### 3.2 Windows Service (OpenAutomate.BotAgent.Service)

#### 3.2.1 Service Architecture

The service is implemented as a .NET 8 BackgroundService with dependency injection:

- **Worker**: Main service implementation
  - Handles startup and shutdown procedures
  - Maintains the event loop for periodic tasks
  - Manages component lifecycle

- **ApiServer**: Local HTTP API implementation
  - Handles incoming requests from UI and scripts
  - Implements authentication and authorization
  - Exposes endpoints for various functionality

#### 3.2.2 Core Components

- **AssetManager**: Manages secure handling of credentials and assets
  - Caches assets locally with encryption
  - Retrieves assets from central server as needed
  - Handles secure memory management

- **ExecutionManager**: Controls automation execution
  - Launches and monitors automation processes
  - Captures output and status information
  - Handles resource management and cleanup

- **MachineKeyManager**: Manages the agent's identity
  - Securely stores the machine key
  - Handles authentication with the server
  - Implements server registration protocol

### 3.3 Common Library (OpenAutomate.BotAgent.Common)

Shared code and models used by both UI and Service:

- **Configuration Models**: Shared data structures
  - `BotAgentConfig`: Configuration options for service and UI
  - `ConnectionConfig`: Server connection parameters

- **DTOs**: Data transfer objects for API communication
  - `AssetDto`: Representation of secure assets
  - `ExecutionStatusDto`: Execution state information

- **Interfaces**: Service contracts for dependency injection
  - `IAssetManager`: Asset management interface
  - `IExecutionManager`: Execution control interface
  - `IServerCommunication`: Communication protocol interface

### 3.4 Python SDK (OpenAutomate.BotAgent.SDK)

The Python SDK provides a simple interface for automation scripts:

```python
from openautomateagent import Client

# Initialize client with default localhost connection
client = Client()

# Get secure asset from server
db_password = client.get_asset("db_password")

# Log information to central server
client.log("Starting database operation", level="info")

# Update execution status
client.update_status("processing_data")

# Report metrics
client.report_metric("records_processed", 1500)
```

#### 3.4.1 SDK Architecture

- **Client**: Main entry point for SDK functionality
  - Handles local API communication
  - Implements retry and error handling
  - Manages connection lifecycle

- **Asset Manager**: Handles secure asset retrieval
  - Caches assets for performance
  - Handles encryption and secure storage
  - Implements lease management for credentials

- **Logger**: Centralized logging functionality
  - Sends logs to local service and central server
  - Implements log levels and filtering
  - Handles offline logging and synchronization

## 4. Security Architecture

### 4.1 Authentication & Authorization

- **Machine Key Authentication**: Each agent has a unique machine key
  - Generated during agent registration
  - Used for all communication with central server
  - Stored securely using Windows DPAPI

- **Local API Security**:
  - Localhost-only binding for API server
  - Authentication token required for sensitive operations
  - Request validation and sanitization

### 4.2 Asset Security

- **Secure Storage**:
  - Assets stored in memory when possible
  - Disk caching uses strong encryption (AES-256)
  - Memory protection using SecureString and pinning

- **Access Control**:
  - Time-limited leases for sensitive credentials
  - Audit logging for all asset access
  - Granular permissions for scripts and processes

### 4.3 Communication Security

- **Server Communication**:
  - TLS 1.3 for all communication
  - Certificate validation and pinning
  - Request signing and replay protection

- **Local Communication**:
  - HTTPS for local API with self-signed certificate
  - Process-level authentication for local requests
  - Input validation and sanitization

## 5. Data Flow

### 5.1 Agent Registration

1. User enters machine key in UI application
2. UI sends registration request to OpenAutomate server
3. Server validates machine key and registers agent
4. Service stores registration information securely
5. Agent is now authorized for automation execution

### 5.2 Automation Execution

1. OpenAutomate server schedules automation task
2. Server sends execution request to agent
3. Agent's ExecutionManager prepares execution environment
4. Python script is launched with appropriate context
5. Script uses SDK to interact with agent
6. Agent reports status updates to server
7. Execution results are captured and reported

### 5.3 Asset Retrieval

1. Automation script requests asset through SDK
2. SDK sends request to local API server
3. Service validates request and authorization
4. Service retrieves asset from cache or central server
5. Asset is securely returned to requesting script
6. Access is logged for audit purposes

## 6. Performance Considerations

### 6.1 Resource Management

- **Memory Usage**:
  - Configurable cache sizes for assets and execution data
  - Proper disposal of unmanaged resources
  - Memory pressure detection and adaptation

- **CPU Utilization**:
  - Background operations use low priority
  - Execution throttling based on system load
  - Configurable limits for concurrent operations

### 6.2 Scalability

- **Concurrent Execution**:
  - Supports multiple simultaneous automation tasks
  - Resource isolation between executions
  - Fair scheduling based on priority

- **Workload Management**:
  - Queue-based execution for high-volume scenarios
  - Backpressure mechanisms for overload protection
  - Graceful degradation during resource constraints

## 7. Error Handling & Resilience

### 7.1 Error Recovery

- **Service Recovery**:
  - Automatic service restart on failure
  - State persistence for recovery
  - Graceful shutdown and cleanup

- **Connection Resilience**:
  - Automatic reconnection with exponential backoff
  - Connection status monitoring and health checks
  - Offline operation support with synchronization

### 7.2 Logging & Diagnostics

- **Comprehensive Logging**:
  - Structured logging with contextual information
  - Log levels (Debug, Info, Warning, Error, Critical)
  - Log rotation and retention policies

- **Diagnostics**:
  - Health check endpoints for monitoring
  - Performance counters for operational metrics
  - Troubleshooting utilities and diagnostic data collection

## 8. Deployment Architecture

### 8.1 Installation

- **MSI Installer**:
  - Silent installation option for automated deployment
  - Configurable installation parameters
  - Proper handling of upgrades and downgrades

- **Service Registration**:
  - Proper Windows service registration
  - Appropriate security context for service operation
  - Startup configuration and recovery settings

### 8.2 Updates

- **Self-update Mechanism**:
  - Version checking with central server
  - Automatic download of updates
  - Coordinated update process with minimal downtime

- **Configuration Updates**:
  - Remote configuration management
  - Policy-based settings enforcement
  - Configuration version control

## 9. Development Guidelines

### 9.1 Coding Standards

- **C# Coding Standards**:
  - Follow Microsoft's C# Coding Conventions
  - Use C# 10+ language features appropriately
  - Implement proper exception handling and resource management

- **Python Coding Standards**:
  - Follow PEP 8 style guide
  - Support Python 3.8+ compatibility
  - Implement proper error handling and resource cleanup

### 9.2 Testing Strategy

- **Unit Testing**:
  - High unit test coverage (target >80%)
  - Mock-based testing for external dependencies
  - Test both happy path and error scenarios

- **Integration Testing**:
  - End-to-end testing of major workflows
  - Environment isolation for reliable testing
  - Performance and stress testing

### 9.3 Documentation

- **Code Documentation**:
  - XML documentation for public APIs
  - Clear method and class documentation
  - Examples for complex functionality

- **User Documentation**:
  - Installation and setup guide
  - Administration and troubleshooting guide
  - SDK documentation with examples

## 10. Future Enhancements

### 10.1 Planned Improvements

- **Enhanced Monitoring**:
  - Real-time monitoring dashboard
  - Advanced metrics and performance analysis
  - Resource utilization tracking and optimization

- **Extended SDK Capabilities**:
  - Additional programming language support
  - Extended automation capabilities
  - Enhanced error handling and debugging

- **Integration Expansions**:
  - Integration with additional enterprise systems
  - Support for additional authentication methods
  - Enhanced reporting and analytics capabilities

## 11. Conclusion

The OpenAutomate BotAgent design provides a robust, secure, and scalable solution for distributed automation execution. By separating concerns between the Windows Service and UI components, it achieves both reliability for background operations and a user-friendly interface for configuration and monitoring. The agent's security architecture ensures that sensitive credentials and operations are properly protected, while the Python SDK enables straightforward integration with automation scripts. 
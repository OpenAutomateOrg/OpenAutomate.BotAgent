# OpenAutomate BotAgent - Development Guide

## 1. Introduction

This development guide provides instructions and best practices for developing components of the OpenAutomate BotAgent system. The BotAgent is a distributed agent solution designed to execute automation tasks on local machines while maintaining secure communication with a central OpenAutomate server.

## 2. Getting Started

### 2.1 Development Environment Setup

1. **Prerequisites**
   - Visual Studio 2022 or later
   - .NET 8.0 SDK
   - Python 3.8+ (for SDK development)
   - WiX Toolset v3.11 or later (for installer development)
   - Git

2. **Clone the Repository**
   ```bash
   git clone https://github.com/yourusername/OpenAutomate.BotAgent.git
   cd OpenAutomate.BotAgent
   ```

3. **Open the Solution**
   - Open `OpenAutomate.BotAgent.sln` in Visual Studio
   - Restore NuGet packages
   - Build the solution to verify everything is working correctly

### 2.2 Solution Structure

The solution is organized into the following projects:

- **OpenAutomate.BotAgent.Service**: Windows Service background process
- **OpenAutomate.BotAgent.UI**: WPF application for user interface
- **OpenAutomate.BotAgent.Common**: Shared code, models, and interfaces
- **OpenAutomate.BotAgent.Installer**: WiX installer project
- **OpenAutomate.BotAgent.SDK**: Python SDK for automation scripts
- **OpenAutomate.BotAgent.Api**: API interfaces for server communication

## 3. Coding Standards

### 3.1 C# Coding Standards

- Follow Microsoft's C# Coding Conventions
- Use PascalCase for class names, method names, and public members
- Use camelCase for local variables and private fields
- Use UPPERCASE for constants
- Prefix interface names with "I" (e.g., `IAssetManager`)
- Use C# 10+ features appropriately (record types, pattern matching, etc.)
- Use asynchronous programming with async/await for I/O-bound operations
- Use LINQ and lambda expressions for collection operations
- Follow the clean architecture pattern, enforcing separation of concerns

### 3.2 Naming Conventions

#### File Naming
- Service interfaces: `IServiceName.cs`
- Service implementations: `ServiceName.cs`
- Model classes: `ModelName.cs`
- DTO classes: `ModelNameDto.cs`
- Controllers: `FeatureController.cs`

#### Project Structure
- Group related files in appropriate folders (Models, Services, Controllers, etc.)
- Use feature folders where appropriate for complex features

### 3.3 Documentation

- Add XML documentation for all public APIs and classes
- Include summary documentation for all public methods
- Document parameters and return values
- Add usage examples for complex functionality
- Update README.md and other documentation when making significant changes

## 4. Development Workflow

### 4.1 Windows Service Development

The Windows Service is the core component responsible for running in the background and maintaining communication with the OpenAutomate server.

#### Service Architecture

The service uses a `BackgroundService` implementation:

```csharp
public class BotAgentService : BackgroundService
{
    private readonly ILogger<BotAgentService> _logger;
    private readonly IApiServer _apiServer;
    private readonly IServerCommunication _serverComm;
    private readonly IAssetManager _assetManager;
    private readonly IExecutionManager _executionManager;
    private readonly IMachineKeyManager _machineKeyManager;
    
    public BotAgentService(
        ILogger<BotAgentService> logger,
        IApiServer apiServer,
        IServerCommunication serverComm,
        IAssetManager assetManager,
        IExecutionManager executionManager,
        IMachineKeyManager machineKeyManager)
    {
        // Initialize dependencies
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initialize service and run the main loop
        
        // Start API server
        await _apiServer.StartAsync();
        
        // Main service loop
        while (!stoppingToken.IsCancellationRequested)
        {
            // Perform periodic tasks
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        
        // Cleanup resources
        await _apiServer.StopAsync();
    }
}
```

#### Implementing Core Components

To implement core service components:

1. **Define Interface**
   ```csharp
   public interface IAssetManager
   {
       Task<bool> SyncAssetsAsync();
       Task<string> GetAssetAsync(string key);
       // Other methods...
   }
   ```

2. **Implement Service**
   ```csharp
   public class AssetManager : IAssetManager
   {
       private readonly ILogger<AssetManager> _logger;
       private readonly IServerCommunication _serverComm;
       
       public AssetManager(ILogger<AssetManager> logger, IServerCommunication serverComm)
       {
           _logger = logger;
           _serverComm = serverComm;
       }
       
       public async Task<bool> SyncAssetsAsync()
       {
           // Implementation
       }
       
       public async Task<string> GetAssetAsync(string key)
       {
           // Implementation
       }
   }
   ```

3. **Register the Service**
   ```csharp
   services.AddSingleton<IAssetManager, AssetManager>();
   ```

### 4.2 Local API Server Development

The Local API Server provides HTTP endpoints for UI and scripts to interact with the service.

#### API Server Implementation

```csharp
public class ApiServer : IApiServer, IDisposable
{
    private readonly ILogger<ApiServer> _logger;
    private readonly IOptions<BotAgentConfig> _config;
    private WebApplication _app;
    
    public ApiServer(
        ILogger<ApiServer> logger,
        IOptions<BotAgentConfig> config)
    {
        _logger = logger;
        _config = config;
    }
    
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        
        // Add services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        
        // Configure server URL (localhost only)
        builder.WebHost.UseUrls($"http://localhost:{_config.Value.ApiPort}");
        
        _app = builder.Build();
        
        // Configure endpoints
        ConfigureEndpoints();
        
        await _app.StartAsync();
    }
    
    private void ConfigureEndpoints()
    {
        // Define API routes
        var group = _app.MapGroup("/api");
        
        // Define endpoints
        group.MapGet("/status", GetStatus);
    }
    
    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
        }
    }
    
    public void Dispose()
    {
        _app?.Dispose();
    }
}
```

#### Controller Development

For controller-based API development:

1. **Create a Controller**
   ```csharp
   [ApiController]
   [Route("api/[controller]")]
   public class AssetsController : ControllerBase
   {
       private readonly IAssetManager _assetManager;
       
       public AssetsController(IAssetManager assetManager)
       {
           _assetManager = assetManager;
       }
       
       [HttpGet("{key}")]
       public async Task<IActionResult> GetAsset(string key)
       {
           var value = await _assetManager.GetAssetAsync(key);
           if (value == null)
               return NotFound();
               
           return Ok(new { Key = key, Value = value });
       }
   }
   ```

2. **Register the Controller**
   ```csharp
   services.AddControllers();
   app.MapControllers();
   ```

### 4.3 WPF UI Development

The WPF UI provides user-friendly configuration and monitoring.

#### MVVM Pattern Implementation

1. **View Model**
   ```csharp
   public class MainViewModel : INotifyPropertyChanged
   {
       private readonly ApiClient _apiClient;
       private string _status;
       
       public string Status
       {
           get => _status;
           set
           {
               if (_status != value)
               {
                   _status = value;
                   OnPropertyChanged();
               }
           }
       }
       
       public ICommand ConnectCommand { get; }
       
       public MainViewModel(ApiClient apiClient)
       {
           _apiClient = apiClient;
           ConnectCommand = new RelayCommand(Connect, CanConnect);
       }
       
       private async void Connect()
       {
           // Implementation
       }
       
       private bool CanConnect()
       {
           // Implementation
           return true;
       }
       
       public event PropertyChangedEventHandler PropertyChanged;
       
       protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
       {
           PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
       }
   }
   ```

2. **XAML View**
   ```xml
   <Window x:Class="OpenAutomate.BotAgent.UI.MainWindow"
           xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
           Title="OpenAutomate Bot Agent" Width="800" Height="600">
       <Grid>
           <StackPanel Margin="20">
               <TextBlock Text="Status:" FontWeight="Bold" />
               <TextBlock Text="{Binding Status}" Margin="0,5,0,15" />
               <Button Content="Connect" Command="{Binding ConnectCommand}" Width="100" HorizontalAlignment="Left" />
           </StackPanel>
       </Grid>
   </Window>
   ```

3. **Command Implementation**
   ```csharp
   public class RelayCommand : ICommand
   {
       private readonly Action _execute;
       private readonly Func<bool> _canExecute;
       
       public RelayCommand(Action execute, Func<bool> canExecute = null)
       {
           _execute = execute ?? throw new ArgumentNullException(nameof(execute));
           _canExecute = canExecute;
       }
       
       public bool CanExecute(object parameter)
       {
           return _canExecute?.Invoke() ?? true;
       }
       
       public void Execute(object parameter)
       {
           _execute();
       }
       
       public event EventHandler CanExecuteChanged
       {
           add { CommandManager.RequerySuggested += value; }
           remove { CommandManager.RequerySuggested -= value; }
       }
       
       public void RaiseCanExecuteChanged()
       {
           CommandManager.InvalidateRequerySuggested();
       }
   }
   ```

### 4.4 Python SDK Development

The Python SDK provides a simple interface for automation scripts.

#### SDK Structure

```
OpenAutomate.BotAgent.SDK/
├── openautomateagent/
│   ├── __init__.py
│   ├── client.py
│   ├── asset_manager.py
│   ├── logger.py
│   └── exceptions.py
├── setup.py
└── README.md
```

#### Client Implementation

```python
import requests
import json
import logging
from typing import Dict, Any, Optional

class Client:
    """Main client for interacting with the OpenAutomate BotAgent"""
    
    def __init__(self, base_url: str = "http://localhost:8080/api"):
        """Initialize the client with the local API URL"""
        self.base_url = base_url
        self.logger = logging.getLogger("openautomateagent")
        
    def get_asset(self, key: str) -> str:
        """Get an asset value by key"""
        response = requests.get(f"{self.base_url}/assets/{key}")
        if response.status_code == 200:
            return response.json().get("value")
        elif response.status_code == 404:
            raise KeyError(f"Asset '{key}' not found")
        else:
            raise RuntimeError(f"Failed to retrieve asset: {response.text}")
    
    def update_status(self, status: str) -> bool:
        """Update execution status"""
        response = requests.post(
            f"{self.base_url}/execution/status",
            json={"status": status}
        )
        return response.status_code == 200
    
    def log(self, message: str, level: str = "info") -> None:
        """Log a message to the central server"""
        response = requests.post(
            f"{self.base_url}/logs",
            json={"message": message, "level": level}
        )
        if response.status_code != 200:
            self.logger.warning(f"Failed to send log: {response.text}")
```

#### Setup Script

```python
from setuptools import setup, find_packages

setup(
    name="openautomateagent",
    version="0.1.0",
    packages=find_packages(),
    install_requires=[
        "requests>=2.25.0",
    ],
    author="OpenAutomate Team",
    author_email="support@openautomateorg.com",
    description="SDK for interacting with OpenAutomate BotAgent",
    keywords="automation, bot, agent",
    url="https://github.com/openautomateorg/botagent-sdk",
    classifiers=[
        "Development Status :: 3 - Alpha",
        "Intended Audience :: Developers",
        "License :: OSI Approved :: MIT License",
        "Programming Language :: Python :: 3",
        "Programming Language :: Python :: 3.8",
    ],
    python_requires=">=3.8",
)
```

## 5. Testing Guidelines

### 5.1 Unit Testing

Write unit tests for all services and components:

```csharp
[Fact]
public async Task AssetManager_GetAsset_ReturnsValue()
{
    // Arrange
    var serverCommMock = new Mock<IServerCommunication>();
    serverCommMock
        .Setup(x => x.GetAssetAsync("test-key"))
        .ReturnsAsync("test-value");
    
    var loggerMock = new Mock<ILogger<AssetManager>>();
    
    var assetManager = new AssetManager(loggerMock.Object, serverCommMock.Object);
    
    // Act
    var result = await assetManager.GetAssetAsync("test-key");
    
    // Assert
    Assert.Equal("test-value", result);
}
```

### 5.2 Integration Testing

Test integration between components:

```csharp
[Fact]
public async Task ApiServer_GetAsset_ReturnsAssetFromManager()
{
    // Arrange
    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAssetManager>(new MockAssetManager());
            });
        });
    
    var client = factory.CreateClient();
    
    // Act
    var response = await client.GetAsync("/api/assets/test-key");
    
    // Assert
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync();
    var asset = JsonSerializer.Deserialize<AssetDto>(content);
    Assert.Equal("test-key", asset.Key);
    Assert.Equal("test-value", asset.Value);
}
```

### 5.3 End-to-End Testing

Test complete user workflows:

```csharp
[Fact]
public async Task ConnectAndRetrieveAsset_WorksCorrectly()
{
    // This test requires a running test server
    // Implementation details would depend on your testing framework
}
```

## 6. Security Implementation Guidelines

### 6.1 Machine Key Authentication

```csharp
public class MachineKeyManager : IMachineKeyManager
{
    private readonly IOptions<BotAgentConfig> _config;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly string _keyPath;
    
    public MachineKeyManager(
        IOptions<BotAgentConfig> config,
        IDataProtectionProvider dataProtection)
    {
        _config = config;
        _dataProtection = dataProtection;
        _keyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAutomate",
            "machinekey.dat");
    }
    
    public bool HasMachineKey()
    {
        return File.Exists(_keyPath);
    }
    
    public string GetMachineKey()
    {
        if (!HasMachineKey())
            return null;
            
        var protector = _dataProtection.CreateProtector("MachineKey");
        var encryptedKey = File.ReadAllText(_keyPath);
        return protector.Unprotect(encryptedKey);
    }
    
    public void SaveMachineKey(string machineKey)
    {
        var protector = _dataProtection.CreateProtector("MachineKey");
        var encryptedKey = protector.Protect(machineKey);
        
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath));
        File.WriteAllText(_keyPath, encryptedKey);
    }
}
```

### 6.2 Asset Encryption

```csharp
public class AssetManager : IAssetManager
{
    private readonly IDataProtectionProvider _dataProtection;
    // Other dependencies...
    
    public AssetManager(
        IDataProtectionProvider dataProtection,
        // Other parameters...)
    {
        _dataProtection = dataProtection;
        // Initialize other dependencies...
    }
    
    public async Task<string> GetAssetAsync(string key)
    {
        var encryptedValue = await _cacheStorage.GetAsync(key);
        if (encryptedValue != null)
        {
            var protector = _dataProtection.CreateProtector("Assets");
            return protector.Unprotect(encryptedValue);
        }
        
        // Retrieve from server if not cached
        // ...
    }
    
    public async Task CacheAssetAsync(string key, string value)
    {
        var protector = _dataProtection.CreateProtector("Assets");
        var encryptedValue = protector.Protect(value);
        await _cacheStorage.SetAsync(key, encryptedValue);
    }
}
```

## 7. Server Communication

### 7.1 Server Communication Implementation

```csharp
public class ServerCommunication : IServerCommunication
{
    private readonly HttpClient _httpClient;
    private readonly IMachineKeyManager _machineKeyManager;
    private readonly IOptions<BotAgentConfig> _config;
    private bool _isConnected;
    
    public ServerCommunication(
        HttpClient httpClient,
        IMachineKeyManager machineKeyManager,
        IOptions<BotAgentConfig> config)
    {
        _httpClient = httpClient;
        _machineKeyManager = machineKeyManager;
        _config = config;
    }
    
    public bool IsConnected => _isConnected;
    
    public async Task<bool> ConnectAsync()
    {
        var machineKey = _machineKeyManager.GetMachineKey();
        if (string.IsNullOrEmpty(machineKey))
            return false;
            
        var request = new
        {
            MachineKey = machineKey,
            MachineName = Environment.MachineName
        };
        
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_config.Value.ServerUrl}/api/botAgent/connect", request);
                
            _isConnected = response.IsSuccessStatusCode;
            return _isConnected;
        }
        catch
        {
            _isConnected = false;
            return false;
        }
    }
    
    public async Task<bool> SendHealthCheckAsync()
    {
        if (!IsConnected)
            return false;
            
        var machineKey = _machineKeyManager.GetMachineKey();
        var request = new
        {
            MachineKey = machineKey,
            Status = "Online",
            Timestamp = DateTime.UtcNow
        };
        
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_config.Value.ServerUrl}/api/botAgent/health", request);
                
            return response.IsSuccessStatusCode;
        }
        catch
        {
            _isConnected = false;
            return false;
        }
    }
}
```

### 7.2 HTTP Client Configuration

```csharp
services.AddHttpClient<IServerCommunication, ServerCommunication>(client =>
{
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
        
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Configure TLS and certificate validation
    ServerCertificateCustomValidationCallback = 
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});
```

## 8. Deployment & Packaging

### 8.1 WiX Installer

To create the Windows installer:

1. **Configure WiX Project**
   - Set product details (name, version, manufacturer)
   - Define installation directory
   - Configure service installation

2. **Add Files to the Installer**
   ```xml
   <Component Id="ServiceComponent" Guid="*" Directory="INSTALLFOLDER">
     <File Id="ServiceExe" Source="$(var.OpenAutomate.BotAgent.Service.TargetPath)" KeyPath="yes" />
     <ServiceInstall
       Id="ServiceInstaller"
       Type="ownProcess"
       Name="OpenAutomateBotAgent"
       DisplayName="OpenAutomate Bot Agent"
       Description="Executes automation tasks for OpenAutomate"
       Start="auto"
       Account="LocalSystem"
       ErrorControl="normal" />
     <ServiceControl
       Id="ServiceControl"
       Name="OpenAutomateBotAgent"
       Start="install"
       Stop="both"
       Remove="uninstall"
       Wait="yes" />
   </Component>
   ```

3. **Add UI Application**
   ```xml
   <Component Id="UIComponent" Guid="*" Directory="INSTALLFOLDER">
     <File Id="UIExe" Source="$(var.OpenAutomate.BotAgent.UI.TargetPath)" KeyPath="yes">
       <Shortcut Id="StartMenuShortcut"
                 Name="OpenAutomate Bot Agent"
                 Directory="ProgramMenuFolder"
                 WorkingDirectory="INSTALLFOLDER"
                 Advertise="yes" />
     </File>
   </Component>
   ```

## 9. Troubleshooting & Development Tips

### 9.1 Debugging the Windows Service

1. **Debug as Console Application**
   - Add conditional compilation to Program.cs:

   ```csharp
   public static void Main(string[] args)
   {
       var builder = Host.CreateApplicationBuilder(args);
       
       #if DEBUG
       builder.Services.AddHostedService<Worker>();
       #else
       builder.UseWindowsService();
       builder.Services.AddHostedService<Worker>();
       #endif
       
       var host = builder.Build();
       host.Run();
   }
   ```

2. **Install and Debug a Running Service**
   - Install the service using the installer
   - Attach the Visual Studio debugger to the running process

### 9.2 Logging Best Practices

```csharp
// Configure logging
services.AddLogging(builder =>
{
    builder.AddFile("logs/botagent-{Date}.log", LogLevel.Information);
    builder.AddEventLog(options =>
    {
        options.SourceName = "OpenAutomate Bot Agent";
    });
});

// Use logging in components
public class AssetManager : IAssetManager
{
    private readonly ILogger<AssetManager> _logger;
    
    public AssetManager(ILogger<AssetManager> logger)
    {
        _logger = logger;
    }
    
    public async Task<string> GetAssetAsync(string key)
    {
        _logger.LogInformation("Retrieving asset: {Key}", key);
        
        try
        {
            // Implementation
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve asset: {Key}", key);
            throw;
        }
    }
}
```

## 10. Contribution Guidelines

### 10.1 Pull Request Process

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Make your changes
4. Run tests to ensure they pass
5. Commit your changes (`git commit -am 'Add feature'`)
6. Push to the branch (`git push origin feature/my-feature`)
7. Create a new Pull Request

### 10.2 Code Review Checklist

- Does the code follow the established coding standards?
- Are there appropriate unit tests?
- Is the code well-documented?
- Are changes sufficiently logged?
- Are security considerations properly addressed?
- Is the code performant?

## 11. Conclusion

This development guide provides the essential information for developing components of the OpenAutomate BotAgent system. By following these guidelines, you can create consistent, secure, and maintainable code that integrates seamlessly with the existing architecture.

For more detailed information on specific components or workflows, refer to the TechnicalDesign.md document and the Implementation.md document.

## 12. Resources

- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [ASP.NET Core Documentation](https://docs.microsoft.com/en-us/aspnet/core/)
- [WPF Documentation](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
- [WiX Toolset Documentation](https://wixtoolset.org/documentation/)
- [Python Documentation](https://docs.python.org/3/) 
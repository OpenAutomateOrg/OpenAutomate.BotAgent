# OpenAutomate BotAgent Implementation Document

## 1. Overview

The OpenAutomate Bot Agent is a distributed component responsible for executing automation on target machines. It consists of two primary components:
1. A Windows Service that runs in the background
2. A WPF UI application for configuration and monitoring

These components communicate through a local API server that also enables Python scripts to interact with the Bot Agent service.

## 2. Component Implementation Details

### 2.1 Windows Service (OpenAutomate.BotAgent.Service)

#### 2.1.1 Service Host (Program.cs)
```csharp
public static void Main(string[] args)
{
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "OpenAutomateBotAgent";
        })
        .ConfigureServices((hostContext, services) =>
        {
            // Register core services
            services.AddSingleton<IAssetManager, AssetManager>();
            services.AddSingleton<IExecutionManager, ExecutionManager>();
            services.AddSingleton<IServerCommunication, ServerCommunication>();
            services.AddSingleton<IMachineKeyManager, MachineKeyManager>();
            
            // Register API server
            services.AddSingleton<IApiServer, ApiServer>();
            
            // Register the Windows Service
            services.AddHostedService<BotAgentService>();
            
            // Add configuration
            services.Configure<BotAgentConfig>(
                hostContext.Configuration.GetSection("BotAgent"));
        })
        .Build();

    host.Run();
}
```

#### 2.1.2 Bot Agent Service (BotAgentService.cs)
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
        _logger = logger;
        _apiServer = apiServer;
        _serverComm = serverComm;
        _assetManager = assetManager;
        _executionManager = executionManager;
        _machineKeyManager = machineKeyManager;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bot Agent Service starting");
        
        // Start API server
        await _apiServer.StartAsync();
        
        // Attempt to connect to server if machine key exists
        if (_machineKeyManager.HasMachineKey())
        {
            await _serverComm.ConnectAsync();
            await _assetManager.SyncAssetsAsync();
        }
        
        while (!stoppingToken.IsCancellationRequested)
        {
            // Health check and status update
            if (_serverComm.IsConnected)
            {
                await _serverComm.SendHealthCheckAsync();
                await _executionManager.SendStatusUpdatesAsync();
            }
            
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        
        // Stop API server
        await _apiServer.StopAsync();
        
        _logger.LogInformation("Bot Agent Service stopping");
    }
}
```

#### 2.1.3 Local API Server (ApiServer.cs)
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
        
        // Configure middleware
        _app.UseSwagger();
        _app.UseSwaggerUI();
        
        // Configure endpoints
        ConfigureEndpoints();
        
        await _app.StartAsync();
        _logger.LogInformation("API Server started on port {Port}", _config.Value.ApiPort);
    }
    
    private void ConfigureEndpoints()
    {
        // Define API routes
        var group = _app.MapGroup("/api");
        
        // Status endpoints
        group.MapGet("/status", GetStatus);
        group.MapGet("/health", () => "Healthy");
        
        // Asset endpoints
        group.MapGet("/assets", GetAssets);
        group.MapGet("/assets/{key}", GetAssetByKey);
        
        // Config endpoints
        group.MapGet("/config", GetConfig);
        group.MapPost("/config", UpdateConfig);
        
        // Execution endpoints
        group.MapPost("/execution/start", StartExecution);
        group.MapGet("/execution/{id}", GetExecutionStatus);
        group.MapPost("/execution/{id}/stop", StopExecution);
        
        // Registration endpoints
        group.MapGet("/registration/status", GetRegistrationStatus);
        group.MapPost("/registration", RegisterAgent);
    }
    
    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            _logger.LogInformation("API Server stopped");
        }
    }
    
    public void Dispose()
    {
        _app?.Dispose();
    }
    
    // API endpoint handlers will be implemented here
    // For brevity, these are omitted in this overview
}
```

#### 2.1.4 Asset Manager (AssetManager.cs)
```csharp
public class AssetManager : IAssetManager
{
    private readonly ILogger<AssetManager> _logger;
    private readonly IServerCommunication _serverComm;
    private readonly IMachineKeyManager _machineKeyManager;
    private readonly Dictionary<string, string> _assetCache;
    private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
    
    public AssetManager(
        ILogger<AssetManager> logger,
        IServerCommunication serverComm,
        IMachineKeyManager machineKeyManager)
    {
        _logger = logger;
        _serverComm = serverComm;
        _machineKeyManager = machineKeyManager;
        _assetCache = new Dictionary<string, string>();
    }
    
    public async Task<string> GetAssetAsync(string key)
    {
        await _cacheLock.WaitAsync();
        try
        {
            // Try to get from cache first
            if (_assetCache.TryGetValue(key, out var cachedValue))
            {
                return cachedValue;
            }
            
            // Not in cache, try to get from server
            if (!_serverComm.IsConnected)
            {
                throw new InvalidOperationException("Not connected to server");
            }
            
            var machineKey = _machineKeyManager.GetMachineKey();
            if (string.IsNullOrEmpty(machineKey))
            {
                throw new InvalidOperationException("Machine key not available");
            }
            
            var asset = await _serverComm.GetAssetAsync(key, machineKey);
            if (asset != null)
            {
                _assetCache[key] = asset;
                return asset;
            }
            
            throw new KeyNotFoundException($"Asset with key '{key}' not found");
        }
        finally
        {
            _cacheLock.Release();
        }
    }
    
    public async Task<IEnumerable<string>> GetAllAssetKeysAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (!_serverComm.IsConnected)
            {
                return _assetCache.Keys;
            }
            
            var machineKey = _machineKeyManager.GetMachineKey();
            if (string.IsNullOrEmpty(machineKey))
            {
                return Enumerable.Empty<string>();
            }
            
            var assets = await _serverComm.GetAllAssetsAsync(machineKey);
            return assets?.Select(a => a.Key) ?? Enumerable.Empty<string>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }
    
    public async Task SyncAssetsAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (!_serverComm.IsConnected)
            {
                return;
            }
            
            var machineKey = _machineKeyManager.GetMachineKey();
            if (string.IsNullOrEmpty(machineKey))
            {
                return;
            }
            
            var assets = await _serverComm.GetAllAssetsAsync(machineKey);
            if (assets != null)
            {
                _assetCache.Clear();
                foreach (var asset in assets)
                {
                    _assetCache[asset.Key] = asset.Value;
                }
                
                _logger.LogInformation("Synced {Count} assets from server", _assetCache.Count);
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
```

#### 2.1.5 Machine Key Manager (MachineKeyManager.cs)
```csharp
public class MachineKeyManager : IMachineKeyManager
{
    private readonly ILogger<MachineKeyManager> _logger;
    private readonly IOptions<BotAgentConfig> _config;
    private string _machineKey;
    
    public MachineKeyManager(
        ILogger<MachineKeyManager> logger,
        IOptions<BotAgentConfig> config)
    {
        _logger = logger;
        _config = config;
        LoadMachineKey();
    }
    
    private void LoadMachineKey()
    {
        try
        {
            var keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "machine.key");
                
            if (File.Exists(keyPath))
            {
                // Read and decrypt the machine key using DPAPI
                var encryptedData = File.ReadAllBytes(keyPath);
                var decryptedData = ProtectedData.Unprotect(
                    encryptedData, 
                    null, 
                    DataProtectionScope.LocalMachine);
                _machineKey = Encoding.UTF8.GetString(decryptedData);
                
                _logger.LogInformation("Machine key loaded successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load machine key");
        }
    }
    
    public bool HasMachineKey()
    {
        return !string.IsNullOrEmpty(_machineKey);
    }
    
    public string GetMachineKey()
    {
        return _machineKey;
    }
    
    public void SetMachineKey(string machineKey)
    {
        if (string.IsNullOrEmpty(machineKey))
        {
            throw new ArgumentNullException(nameof(machineKey));
        }
        
        try
        {
            var keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent");
                
            Directory.CreateDirectory(keyPath);
            keyPath = Path.Combine(keyPath, "machine.key");
            
            // Encrypt the machine key using DPAPI
            var dataToEncrypt = Encoding.UTF8.GetBytes(machineKey);
            var encryptedData = ProtectedData.Protect(
                dataToEncrypt,
                null,
                DataProtectionScope.LocalMachine);
                
            File.WriteAllBytes(keyPath, encryptedData);
            
            _machineKey = machineKey;
            _logger.LogInformation("New machine key saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save machine key");
            throw;
        }
    }
    
    public void ClearMachineKey()
    {
        try
        {
            var keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "machine.key");
                
            if (File.Exists(keyPath))
            {
                File.Delete(keyPath);
            }
            
            _machineKey = null;
            _logger.LogInformation("Machine key cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear machine key");
            throw;
        }
    }
}
```

### 2.2 WPF UI Application (OpenAutomate.BotAgent.UI)

#### 2.2.1 Application Entry (App.xaml.cs)
```csharp
public partial class App : Application
{
    private ServiceProvider _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
    
    private void ConfigureServices(IServiceCollection services)
    {
        // Register configuration
        var config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
            
        services.Configure<BotAgentConfig>(config.GetSection("BotAgent"));
        
        // Register services
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<INotificationService, NotificationService>();
        
        // Register view models
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ExecutionViewModel>();
        services.AddTransient<RegistrationViewModel>();
        
        // Register views
        services.AddTransient<MainWindow>();
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
```

#### 2.2.2 API Client (ApiClient.cs)
```csharp
public class ApiClient : IApiClient
{
    private readonly ILogger<ApiClient> _logger;
    private readonly IOptions<BotAgentConfig> _config;
    private readonly HttpClient _httpClient;
    
    public ApiClient(
        ILogger<ApiClient> logger,
        IOptions<BotAgentConfig> config)
    {
        _logger = logger;
        _config = config;
        
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri($"http://localhost:{_config.Value.ApiPort}/api/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
    
    public async Task<StatusModel> GetStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("status");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<StatusModel>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status");
            return null;
        }
    }
    
    public async Task<IEnumerable<AssetModel>> GetAssetsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("assets");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<IEnumerable<AssetModel>>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting assets");
            return Enumerable.Empty<AssetModel>();
        }
    }
    
    public async Task<ConfigurationModel> GetConfigAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("config");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ConfigurationModel>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration");
            return null;
        }
    }
    
    public async Task<bool> UpdateConfigAsync(ConfigurationModel config)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(config),
                Encoding.UTF8,
                "application/json");
                
            var response = await _httpClient.PostAsync("config", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating configuration");
            return false;
        }
    }
    
    public async Task<RegistrationStatusModel> GetRegistrationStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("registration/status");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<RegistrationStatusModel>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting registration status");
            return null;
        }
    }
    
    public async Task<RegistrationResultModel> RegisterAgentAsync(RegistrationRequestModel request)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");
                
            var response = await _httpClient.PostAsync("registration", content);
            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<RegistrationResultModel>(responseContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering agent");
            return null;
        }
    }
}
```

### 2.3 Python SDK (OpenAutomate.BotAgent.SDK)

#### 2.3.1 Client Implementation (client.py)
```python
import requests
import logging
from typing import Optional, Dict, Any, List

class Client:
    """Client for interacting with the OpenAutomate Bot Agent API"""
    
    def __init__(self, host: str = "localhost", port: int = 8080):
        """Initialize the client with the Bot Agent API address"""
        self.base_url = f"http://{host}:{port}/api"
        self.logger = logging.getLogger("OpenAutomateAgent")
        
    def get_asset(self, key: str) -> Optional[str]:
        """
        Get an asset value by key
        
        Args:
            key: The asset key to retrieve
            
        Returns:
            The asset value or None if not found
        """
        try:
            response = requests.get(f"{self.base_url}/assets/{key}")
            response.raise_for_status()
            return response.text
        except requests.RequestException as e:
            self.logger.error(f"Error retrieving asset '{key}': {e}")
            return None
    
    def get_assets(self) -> List[Dict[str, Any]]:
        """
        Get all available assets
        
        Returns:
            List of asset objects with key and metadata
        """
        try:
            response = requests.get(f"{self.base_url}/assets")
            response.raise_for_status()
            return response.json()
        except requests.RequestException as e:
            self.logger.error(f"Error retrieving assets: {e}")
            return []
    
    def update_status(self, status: str, execution_id: Optional[str] = None) -> bool:
        """
        Update the execution status
        
        Args:
            status: The status message to set
            execution_id: Optional ID of the specific execution
            
        Returns:
            True if status was updated successfully
        """
        try:
            url = f"{self.base_url}/execution/{execution_id}/status" if execution_id else f"{self.base_url}/status"
            response = requests.post(url, json={"status": status})
            response.raise_for_status()
            return True
        except requests.RequestException as e:
            self.logger.error(f"Error updating status: {e}")
            return False
    
    def log(self, message: str, level: str = "info") -> bool:
        """
        Send a log message to the Bot Agent
        
        Args:
            message: The log message
            level: Log level (debug, info, warning, error)
            
        Returns:
            True if log was sent successfully
        """
        try:
            response = requests.post(
                f"{self.base_url}/log", 
                json={"message": message, "level": level}
            )
            response.raise_for_status()
            return True
        except requests.RequestException as e:
            self.logger.error(f"Error sending log: {e}")
            return False
```

## 3. Authentication and Security Implementation

### 3.1 Machine Key Authentication Flow

The Bot Agent authenticates with the OpenAutomate server using a machine key:

1. **Registration Process**
   - User enters server URL and tenant credentials in WPF UI
   - UI makes API request to Windows Service
   - Service contacts server with credentials
   - Server generates a unique machine key and returns it
   - Machine key is securely stored using DPAPI

2. **Asset Retrieval Process**
   - Python script requests asset via local API
   - Windows Service validates request
   - Service sends request to server with machine key
   - Server verifies machine key and checks permissions
   - If authorized, asset is returned and passed to script

### 3.2 Security Considerations

1. **Machine Key Protection**
   - Machine keys are 256-bit cryptographically random values
   - Keys are stored encrypted using Windows DPAPI
   - Keys are transmitted only over HTTPS to the server

2. **API Security**
   - Local API only listens on localhost (127.0.0.1)
   - API requests validated with proper input sanitization
   - API access can be token protected for additional security

3. **Asset Protection**
   - Asset values never stored on disk in plain text
   - Assets cached in memory only while Service is running
   - Asset access is logged for audit purposes

4. **Tenant Isolation**
   - Machine keys are validated against tenant ID
   - Bot Agents can only access assets in their tenant
   - Assets are further restricted by Organization Unit

## 4. Installation and Setup Process

### 4.1 Installer Implementation

The installer is built using WiX Toolset and performs these steps:

1. **Prerequisites Check**
   - Verifies .NET 8.0 Runtime is installed
   - Checks for administrative privileges
   - Verifies Windows version compatibility

2. **File Installation**
   - Copies Service binaries to Program Files
   - Copies WPF UI binaries to Program Files
   - Installs Python SDK package

3. **Service Configuration**
   - Registers Windows Service
   - Sets service to start automatically
   - Creates necessary folders with proper permissions

4. **UI Integration**
   - Creates Start Menu shortcuts
   - Sets up file associations if needed
   - Configures auto-start for UI (optional)

### 4.2 First-Run Experience

1. **Service Startup**
   - Windows Service starts automatically after installation
   - Local API server initializes on default port (8080)
   - Service waits for registration

2. **UI First Run**
   - WPF application detects first run
   - Displays welcome screen and setup wizard
   - Guides user through server connection
   - Assists with Bot Agent registration
   - Verifies service connectivity

3. **Registration Process**
   - User enters server URL and credentials
   - UI sends registration request to local API
   - Service communicates with OpenAutomate server
   - Server validates credentials and generates machine key
   - Machine key is securely stored by the service
   - Registration status displayed to user

## 5. Testing Strategy

### 5.1 Unit Testing

1. **Service Component Tests**
   - Test AssetManager with mocked IServerCommunication
   - Test MachineKeyManager storage and retrieval
   - Test ApiServer endpoint routing

2. **UI Component Tests**
   - Test ViewModels with mocked IApiClient
   - Test UI navigation and state management
   - Test configuration validation

3. **SDK Tests**
   - Test Python client with mocked API responses
   - Test error handling and retry logic

### 5.2 Integration Testing

1. **Service Integration Tests**
   - Test API server with real HTTP requests
   - Test Windows Service startup/shutdown
   - Test service recovery mechanisms

2. **End-to-End Tests**
   - Test complete registration flow
   - Test asset retrieval from Python script
   - Test execution monitoring and control

### 5.3 Security Testing

1. **Penetration Testing**
   - Test local API for vulnerabilities
   - Verify machine key protection
   - Validate authentication mechanisms

2. **Compliance Testing**
   - Verify tenant isolation
   - Test audit logging compliance
   - Verify secure credential handling

## 6. Deployment and Updating

### 6.1 Deployment Strategy

1. **Package Distribution**
   - Signed MSI installer package
   - Automated deployment via management tools (optional)
   - Command-line silent installation support

2. **Enterprise Deployment**
   - Group Policy deployment support
   - Preconfigured installation scripts
   - Bulk registration capabilities

### 6.2 Update Mechanism

1. **Service Updates**
   - New versions detected via server
   - Automatic or prompted updates
   - Service gracefully stops and restarts

2. **Update Process**
   - Update downloaded in background
   - Verification of package integrity
   - Backup of configuration
   - Apply update during maintenance window
   - Configuration migration if needed

## 7. Troubleshooting and Monitoring

### 7.1 Logging Implementation

1. **Log Sources**
   - Windows Service logs to Event Log
   - File-based logging with rotation
   - Execution-specific logging

2. **Log Levels**
   - Debug: Detailed diagnostics
   - Information: Normal operations
   - Warning: Potential issues
   - Error: Operation failures
   - Critical: System failures

### 7.2 Monitoring Tools

1. **UI Monitoring Panel**
   - Real-time service status
   - Connection health
   - Recent operations
   - Error notifications

2. **Remote Monitoring**
   - Status reporting to central server
   - Health metrics collection
   - Alert configuration 
import requests
import logging
from typing import Optional, Dict, Any, List, Union

class KeyNotFoundException(Exception):
    """Raised when an asset key is not found"""
    pass

class UnauthorizedException(Exception):
    """Raised when not authorized to access an asset"""
    pass

class ConnectionException(Exception):
    """Raised when there's a connection issue with the Bot Agent API"""
    pass

class Client:
    """Client for interacting with the OpenAutomate Bot Agent API"""
    
    def __init__(self, host: str = "localhost", port: int = 8080):
        """Initialize the client with the Bot Agent API address
        
        Args:
            host: The hostname where the Bot Agent API is running
            port: The port the Bot Agent API is listening on
        """
        self.base_url = f"http://{host}:{port}/api"
        self.logger = logging.getLogger("OpenAutomateAgent")
        
    def get_asset(self, key: str) -> str:
        """Get an asset value by key from the Bot Agent
        
        Args:
            key: The asset key to retrieve
            
        Returns:
            The asset value
            
        Raises:
            KeyNotFoundException: If the asset is not found
            UnauthorizedException: If not authorized to access the asset
            ConnectionException: If there's a connection issue
        """
        try:
            response = requests.get(f"{self.base_url}/assets/{key}")
            
            if response.status_code == 200:
                return response.text
            elif response.status_code == 404:
                error_message = "Asset not found"
                try:
                    error_data = response.json()
                    if "error" in error_data:
                        error_message = error_data["error"]
                except:
                    pass
                raise KeyNotFoundException(error_message)
            elif response.status_code == 403:
                raise UnauthorizedException(f"Not authorized to access asset '{key}'")
            else:
                raise Exception(f"Failed to retrieve asset: {response.status_code}")
                
        except requests.RequestException as e:
            raise ConnectionException(f"Error connecting to Bot Agent: {e}")
    
    def get_all_asset_keys(self) -> List[str]:
        """Get all asset keys available to this Bot Agent
        
        Returns:
            A list of available asset keys
            
        Raises:
            ConnectionException: If there's a connection issue
        """
        try:
            response = requests.get(f"{self.base_url}/assets")
            
            if response.status_code == 200:
                return response.json()
            else:
                error_message = f"Failed to retrieve assets: {response.status_code}"
                try:
                    error_data = response.json()
                    if "error" in error_data:
                        error_message = error_data["error"]
                except:
                    pass
                raise Exception(error_message)
                
        except requests.RequestException as e:
            raise ConnectionException(f"Error connecting to Bot Agent: {e}")
    
    def update_status(self, status: str, execution_id: Optional[str] = None) -> bool:
        """Update the execution status
        
        Args:
            status: The status message to set
            execution_id: Optional ID of the specific execution
            
        Returns:
            True if status was updated successfully
            
        Raises:
            ConnectionException: If there's a connection issue
        """
        try:
            url = f"{self.base_url}/execution/{execution_id}/status" if execution_id else f"{self.base_url}/status"
            response = requests.post(url, json={"status": status})
            return response.status_code == 200
        except requests.RequestException as e:
            raise ConnectionException(f"Error connecting to Bot Agent: {e}")
    
    def log(self, message: str, level: str = "info") -> bool:
        """Log a message through the Bot Agent
        
        Args:
            message: The message to log
            level: The log level (debug, info, warning, error)
            
        Returns:
            True if the message was logged successfully
        """
        try:
            response = requests.post(f"{self.base_url}/log", json={
                "message": message,
                "level": level.lower()
            })
            return response.status_code == 200
        except requests.RequestException as e:
            # Don't raise an exception for logging failures
            self.logger.error(f"Failed to send log: {e}")
            return False
            
# Example usage
if __name__ == "__main__":
    # Configure logging
    logging.basicConfig(level=logging.INFO)
    
    # Create client
    client = Client()
    
    try:
        # Get all available asset keys
        asset_keys = client.get_all_asset_keys()
        print(f"Available assets: {asset_keys}")
        
        # Get specific assets
        if asset_keys:
            for key in asset_keys:
                try:
                    value = client.get_asset(key)
                    print(f"Asset '{key}': {value}")
                except KeyNotFoundException as e:
                    print(f"Asset not found: {e}")
                except UnauthorizedException as e:
                    print(f"Not authorized: {e}")
        
        # Update status
        client.update_status("Example script running")
        
        # Log a message
        client.log("Example script completed", "info")
        
    except ConnectionException as e:
        print(f"Connection error: {e}")
    except Exception as e:
        print(f"Unexpected error: {e}") 